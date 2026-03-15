using Microsoft.Extensions.DependencyInjection;
using MyMediator.Interfaces;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace MyMediator.Types
{
    /// <summary>
    /// Статический класс, реализующий кэширование фабрик пайплайнов обработки запросов.
    /// Позволяет запустить цепочку поведений, связанных с выполнением запросов
    /// </summary>
    static class PipelineFactoryCache
    {
        /// <summary>
        /// Кэш делегатов, выполняющих обработку запроса через пайплайн. Ключ - пара: (тип запроса, тип ответа)
        /// </summary>
        private static readonly ConcurrentDictionary<(Type RequestType, Type ResponseType), Delegate> _cache = new();

        /// <summary>
        /// MethodInfo кэшируем один раз при старте приложения
        /// </summary>
        private static readonly MethodInfo BuildMethod = typeof(PipelineFactoryCache)
            .GetMethod(nameof(Build), BindingFlags.Static | BindingFlags.NonPublic)!;

        /// <summary>
        /// Получает или добавляет в кэш делегат для обработки запроса указанного типа
        /// </summary>
        /// <typeparam name="TResponse">Тип результата обработки запроса.</typeparam>
        /// <param name="requestType">Тип запроса.</param>
        /// <returns>Делегат, выполняющий обработку запроса через зарегистрированный обработчик и цепочку поведений.</returns>
        public static Func<IRequest<TResponse>, IServiceProvider, CancellationToken, Task<TResponse>> GetOrAdd<TResponse>(Type requestType)
        {
            var key = (requestType, typeof(TResponse));

            // Используем object в словаре, чтобы избежать проблем с приведением дженериков
            return (Func<IRequest<TResponse>, IServiceProvider, CancellationToken, Task<TResponse>>)
                _cache.GetOrAdd(key, _ => CreateFactoryDelegate<IRequest<TResponse>, TResponse>(requestType, typeof(TResponse)));
        }

        /// <summary>
        /// Создаем делегат через Expression Tree вместо Invoke
        /// </summary>
        /// <typeparam name="T">IRequest<TResponse></typeparam>
        /// <typeparam name="V">TResponse</typeparam>
        /// <param name="requestType">Тип запроса.</param>
        /// <param name="responseType">Тип результата обработки запроса.</param>
        /// <returns></returns>
        private static Delegate CreateFactoryDelegate<T, V>(Type requestType, Type responseType)
        {
            // 1. Замыкаем MethodInfo с конкретными типами (Build<TRequest, TResponse>)
            var genericMethod = BuildMethod.MakeGenericMethod(requestType, responseType);

            // 2. Создаем выражение вызова: Build<TRequest, TResponse>()
            var callExpression = Expression.Call(genericMethod);

            // 3. Оборачиваем в лямбду без параметров: () => Build<TRequest, TResponse>()
            var lambda = Expression.Lambda<Func<Func<T, IServiceProvider, CancellationToken, Task<V>>>>(
                callExpression
            );

            // 4. Компилируем и сразу выполняем один раз, чтобы получить итоговый пайплайн
            return lambda.Compile()();
        }

        /// <summary>
        /// Строит выражение для выполнения пайплайна: разрешение зависимостей, 
        /// проверка хендлера, обёртывание поведениями и выполнение.
        /// </summary>
        /// <typeparam name="TRequest"></typeparam>
        /// <typeparam name="TResponse"></typeparam>
        /// <returns></returns>
        private static Func<IRequest<TResponse>, IServiceProvider, CancellationToken, Task<TResponse>> Build<TRequest, TResponse>()
            where TRequest : IRequest<TResponse>
        {
            // 1. Создаем инвокеры (они уже скомпилированы в отдельных методах)
            var handlerInvoker = CreateHandlerInvoker<TRequest, TResponse>();
            var behaviorInvoker = CreateBehaviorInvoker<TRequest, TResponse>();

            // 2. Определяем параметры входящей лямбды
            var requestObjParam = Expression.Parameter(typeof(IRequest<TResponse>), "requestObj");
            var spParam = Expression.Parameter(typeof(IServiceProvider), "sp");
            var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

            // 3. Определяем локальные переменные для блока
            var requestVar = Expression.Variable(typeof(TRequest), "request");
            var handlerVar = Expression.Variable(typeof(IRequestHandler<TRequest, TResponse>), "handler");
            var behaviorsVar = Expression.Variable(typeof(IPipelineBehavior<TRequest, TResponse>[]), "behaviors");

            // Методы рефлексии для IServiceProvier и Array
            var getServiceMethod = typeof(IServiceProvider).GetMethod(nameof(IServiceProvider.GetService))!;
            var toArrayMethod = typeof(Enumerable).GetMethod(nameof(Enumerable.ToArray))!
                .MakeGenericMethod(typeof(IPipelineBehavior<TRequest, TResponse>));
            var emptyMethod = typeof(Enumerable).GetMethod(nameof(Enumerable.Empty))!
                .MakeGenericMethod(typeof(IPipelineBehavior<TRequest, TResponse>));

            // 4. Формируем выражения для получения зависимостей
            // cast: (TRequest)requestObj
            var assignRequest = Expression.Assign(requestVar, Expression.Convert(requestObjParam, typeof(TRequest)));

            // get handler
            var assignHandler = Expression.Assign(
                handlerVar,
                Expression.Convert(
                    Expression.Call(spParam, getServiceMethod, Expression.Constant(typeof(IRequestHandler<TRequest, TResponse>))),
                    typeof(IRequestHandler<TRequest, TResponse>)
                )
            );

            // check handler != null
            var handlerNullCheck = Expression.IfThen(
                Expression.Equal(handlerVar, Expression.Constant(null, typeof(IRequestHandler<TRequest, TResponse>))),
                Expression.Throw(
                    Expression.New(
                        typeof(InvalidOperationException).GetConstructor(new[] { typeof(string) })!,
                        Expression.Constant($"Handler for {typeof(TRequest)} not registered.")
                    )
                )
            );

            // get behaviors
            var behaviorsServiceType = typeof(IEnumerable<IPipelineBehavior<TRequest, TResponse>>);
            var assignBehaviors = Expression.Assign(
                behaviorsVar,
                Expression.Convert(
                    Expression.Call(
                        toArrayMethod,
                        Expression.Coalesce(
                            Expression.Convert(
                                Expression.Call(spParam, getServiceMethod, Expression.Constant(behaviorsServiceType)),
                                behaviorsServiceType
                            ),
                            Expression.Call(emptyMethod)
                        )
                    ),
                    typeof(IPipelineBehavior<TRequest, TResponse>[])
                )
            );

            // компилирование итогов
            var compiledLambda = Expression.Lambda<Func<IRequest<TResponse>, IServiceProvider, CancellationToken, Task<TResponse>>>(
                Expression.Block(
                    typeof(Task<TResponse>),
                    new[] { requestVar, handlerVar, behaviorsVar },
                    assignRequest,
                    assignHandler,
                    handlerNullCheck,
                    assignBehaviors,
                    // Вызов вспомогательного метода, который делает цикл
                    Expression.Call(
                        typeof(PipelineFactoryCache).GetMethod(nameof(ExecutePipeline), BindingFlags.Static | BindingFlags.NonPublic)!
                        .MakeGenericMethod(typeof(TRequest), typeof(TResponse)),
                        behaviorsVar,
                        requestVar,
                        handlerVar,
                        Expression.Constant(handlerInvoker),
                        Expression.Constant(behaviorInvoker),
                        ctParam
                    )
                ),
                requestObjParam,
                spParam,
                ctParam
            );

            return compiledLambda.Compile();
        }

        /// <summary>
        /// Вспомогательный метод, вынесенный из лямбды, чтобы избежать замыканий
        /// </summary>
        /// <typeparam name="TRequest"></typeparam>
        /// <typeparam name="TResponse"></typeparam>
        /// <param name="behaviors"></param>
        /// <param name="request"></param>
        /// <param name="handler"></param>
        /// <param name="handlerInvoker"></param>
        /// <param name="behaviorInvoker"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        private static Task<TResponse> ExecutePipeline<TRequest, TResponse>(
            IPipelineBehavior<TRequest, TResponse>[] behaviors,
            TRequest request,
            IRequestHandler<TRequest, TResponse> handler,
            Func<IRequestHandler<TRequest, TResponse>, TRequest, CancellationToken, Task<TResponse>> handlerInvoker,
            Func<IPipelineBehavior<TRequest, TResponse>, TRequest, RequestHandlerDelegate<TResponse>, CancellationToken, Task<TResponse>> behaviorInvoker,
            CancellationToken ct)
            where TRequest : IRequest<TResponse>
        {
            RequestHandlerDelegate<TResponse> next = () => handlerInvoker(handler, request, ct);

            // Цикл остался здесь, но замыкания теперь создаются только на переменные цикла,
            // а не на внешний контекст лямбды (request, ct уже захвачены методом)
            for (int i = behaviors.Length - 1; i >= 0; i--)
            {
                var behavior = behaviors[i];
                var prevNext = next;
                next = () => behaviorInvoker(behavior, request, prevNext, ct);
            }

            return next();
        }

        /// <summary>
        /// Создаёт скомпилированный делегат для вызова метода 'HandleAsync' у обработчика запроса (IRequestHandler)"/>
        /// Использует Expression Trees для генерации эффективного вызова без рефлексии во время выполнения.
        /// </summary>
        /// <typeparam name="TRequest">Тип запроса.</typeparam>
        /// <typeparam name="TResponse">Тип результата обработки запроса.</typeparam>
        /// <returns>Делегат, вызывающий 'HandleAsync' у обработчика запроса.</returns>
        private static Func<IRequestHandler<TRequest, TResponse>, TRequest, CancellationToken, Task<TResponse>>
            CreateHandlerInvoker<TRequest, TResponse>() where TRequest : IRequest<TResponse>
        {
            var h = Expression.Parameter(typeof(IRequestHandler<TRequest, TResponse>), "h");
            var r = Expression.Parameter(typeof(TRequest), "r");
            var c = Expression.Parameter(typeof(CancellationToken), "c");
            var call = Expression.Call(h, nameof(IRequestHandler<TRequest, TResponse>.HandleAsync), null, r, c);
            return Expression.Lambda<Func<IRequestHandler<TRequest, TResponse>, TRequest, CancellationToken, Task<TResponse>>>(call, h, r, c).Compile();
        }

        /// <summary>
        /// Создаёт скомпилированный делегат для вызова метода 'HandleAsync' у обработчика запроса (IPipelineBehavior)"/>
        /// Использует Expression Trees для генерации эффективного вызова без рефлексии во время выполнения.
        /// </summary>
        /// <typeparam name="TRequest">Тип запроса.</typeparam>
        /// <typeparam name="TResponse">Тип результата обработки запроса.</typeparam>
        /// <returns>Делегат, вызывающий 'HandleAsync' у обработчика запроса.</returns>
        private static Func<IPipelineBehavior<TRequest, TResponse>, TRequest, RequestHandlerDelegate<TResponse>, CancellationToken, Task<TResponse>>
            CreateBehaviorInvoker<TRequest, TResponse>() where TRequest : IRequest<TResponse>
        {
            var b = Expression.Parameter(typeof(IPipelineBehavior<TRequest, TResponse>), "b");
            var r = Expression.Parameter(typeof(TRequest), "r");
            var n = Expression.Parameter(typeof(RequestHandlerDelegate<TResponse>), "n");
            var c = Expression.Parameter(typeof(CancellationToken), "c");
            var call = Expression.Call(b, nameof(IPipelineBehavior<TRequest, TResponse>.HandleAsync), null, r, n, c);
            return Expression.Lambda<Func<IPipelineBehavior<TRequest, TResponse>, TRequest, RequestHandlerDelegate<TResponse>, CancellationToken, Task<TResponse>>>(call, b, r, n, c).Compile();
        }
    }
}
