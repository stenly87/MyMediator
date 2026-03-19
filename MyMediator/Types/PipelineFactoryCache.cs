using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualBasic;
using MyMediator.Interfaces;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MyMediator.Types
{
    /// <summary>
    /// Статический класс, реализующий кэширование фабрик пайплайнов обработки запросов.
    /// Позволяет запустить цепочку поведений, связанных с выполнением запросов
    /// </summary>
    internal static class PipelineFactoryCache
    {
        private readonly struct PipelineKey : IEquatable<PipelineKey>
        {
            public readonly Type RequestType;
            public readonly Type ResponseType;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public PipelineKey(Type requestType, Type responseType)
            {
                RequestType = requestType;
                ResponseType = responseType;
            }

            // Сравнение по ссылке — Type всегда один и тот же объект
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Equals(PipelineKey other)
                => RequestType == other.RequestType
                && ResponseType == other.ResponseType;

            public override bool Equals(object? obj) => obj is PipelineKey k && Equals(k);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public override int GetHashCode()
                => RequestType.GetHashCode() * 397 ^ ResponseType.GetHashCode();
        }

        private sealed class PipelineKeyComparer : IEqualityComparer<PipelineKey>
        {
            public static readonly PipelineKeyComparer Instance = new();

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Equals(PipelineKey x, PipelineKey y) => x.Equals(y);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int GetHashCode(PipelineKey obj) => obj.GetHashCode();
        }

        private static readonly ConcurrentDictionary<PipelineKey, Delegate> Cache
            = new(PipelineKeyComparer.Instance);

        private static readonly MethodInfo GetServiceMethod
            = typeof(IServiceProvider).GetMethod(nameof(IServiceProvider.GetService))!;

        private static readonly MethodInfo ToArrayMethod
            = typeof(Enumerable).GetMethod(nameof(Enumerable.ToArray))!;

        private static readonly MethodInfo EmptyMethod
            = typeof(Enumerable).GetMethod(nameof(Enumerable.Empty))!;


        public static Func<IRequest<TResponse>, IServiceProvider, CancellationToken, Task<TResponse>>
            GetOrAdd<TResponse>(Type requestType)
        {
            // ▸ Старое: _cache.GetOrAdd((requestType, typeof(TResponse)), ...)  — 1 alloc/вызов
            // ▸ Новое:  _cache.GetOrAdd(new PipelineKey(...), ...)               — 0 alloc/вызов
            return (Func<IRequest<TResponse>, IServiceProvider, CancellationToken, Task<TResponse>>)
                Cache.GetOrAdd(new PipelineKey(requestType, typeof(TResponse)),
                               static key => Build(key.RequestType, key.ResponseType));
        }


        private static Delegate Build(Type requestType, Type responseType)
        {
            var buildMethod = typeof(PipelineFactoryCache)
                .GetMethod(nameof(BuildTyped), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(requestType, responseType);

            // Прямой вызов BuildTyped<TReq, TRes>() через MethodInfo.
            // НЕ Expression.Lambda(() => BuildTyped()).Compile()() — это лишний delegate.
            return (Delegate)buildMethod.Invoke(null, null)!;
        }

        private static Func<IRequest<TRes>, IServiceProvider, CancellationToken, Task<TRes>>
            BuildTyped<TReq, TRes>()
            where TReq : IRequest<TRes>
        {
            // ── Делегаты создаются ОДИН раз при старте (кэшируются в замыкании лямбды) ──

            var handlerInvoker = CreateHandlerInvoker<TReq, TRes>();
            var behaviorInvoker = CreateBehaviorInvoker<TReq, TRes>();

            // ── Expression Tree ──

            var requestObjParam = Expression.Parameter(typeof(IRequest<TRes>), "requestObj");
            var spParam = Expression.Parameter(typeof(IServiceProvider), "sp");
            var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

            var requestVar = Expression.Variable(typeof(TReq), "request");
            var handlerVar = Expression.Variable(typeof(IRequestHandler<TReq, TRes>), "handler");
            var behaviorsVar = Expression.Variable(typeof(IPipelineBehavior<TReq, TRes>[]), "behaviors");

            // 1. request = (TReq)requestObj
            var castRequest = Expression.Assign(requestVar,
                Expression.Convert(requestObjParam, typeof(TReq)));

            // 2. handler = (IRequestHandler<TReq, TRes>)sp.GetService(typeof(...))
            var assignHandler = Expression.Assign(handlerVar,
                Expression.Convert(
                    Expression.Call(spParam, GetServiceMethod,
                        Expression.Constant(typeof(IRequestHandler<TReq, TRes>))),
                    typeof(IRequestHandler<TReq, TRes>)));

            // 3. if (handler == null) throw
            var handlerNullCheck = Expression.IfThen(
                Expression.Equal(handlerVar,
                    Expression.Constant(null, typeof(IRequestHandler<TReq, TRes>))),
                Expression.Throw(
                    Expression.New(
                        typeof(InvalidOperationException)
                            .GetConstructor(new[] { typeof(string) })!,
                        Expression.Constant($"Handler for {typeof(TReq).Name} not registered."))));

            // 4. behaviors = ToArray(sp.GetService(IEnumerable<...>) ?? Empty())
            var behaviorsServiceType = typeof(IEnumerable<IPipelineBehavior<TReq, TRes>>);
            var toArrayClosed = ToArrayMethod.MakeGenericMethod(typeof(IPipelineBehavior<TReq, TRes>));
            var emptyClosed = EmptyMethod.MakeGenericMethod(typeof(IPipelineBehavior<TReq, TRes>));

            var assignBehaviors = Expression.Assign(behaviorsVar,
                Expression.Convert(
                    Expression.Call(toArrayClosed,
                        Expression.Coalesce(
                            Expression.Convert(
                                Expression.Call(spParam, GetServiceMethod,
                                    Expression.Constant(behaviorsServiceType)),
                                behaviorsServiceType),
                            Expression.Convert(
                                Expression.Call(emptyClosed),
                                behaviorsServiceType))),
                    typeof(IPipelineBehavior<TReq, TRes>[])));

            // 5. ExecutePipeline(behaviors, request, handler, handlerInvoker, behaviorInvoker, ct)
            //
            //    ▸ handlerInvoker / behaviorInvoker — Expression.Constant(Delegate)
            //      Expression compiler загружает делегат из замыкания (поле) и передаёт
            //      как аргумент в ExecutePipeline. ExecutePipeline вызывает их через
            //      обычный delegate.Invoke() — НЕ DynamicInvoke.
            //
            //    ▸ Expression.Call(MethodInfo, ...) — компилируется в прямой call IL.
            //
            var execMethod = typeof(PipelineFactoryCache)
                .GetMethod(nameof(ExecutePipeline), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(typeof(TReq), typeof(TRes));

            var callExec = Expression.Call(execMethod,
                behaviorsVar,
                requestVar,
                handlerVar,
                Expression.Constant(handlerInvoker),     // ← делегат, созданный ОДИН раз
                Expression.Constant(behaviorInvoker),    // ← делегат, созданный ОДИН раз
                ctParam);

            var body = Expression.Block(typeof(Task<TRes>),
                new[] { requestVar, handlerVar, behaviorsVar },
                castRequest, assignHandler, handlerNullCheck, assignBehaviors, callExec);

            return Expression
                .Lambda<Func<IRequest<TRes>, IServiceProvider, CancellationToken, Task<TRes>>>(
                    body, requestObjParam, spParam, ctParam)
                .Compile();
        }

        // Замыкания в цикле аллоцируются на каждый вызов (~100 байт × N поведений).
        // Это неизбежно при делегатном подходе, но стоимость мала:
        // Gen0 collection бесплатна для таких объектов.
        private static Task<TRes> ExecutePipeline<TReq, TRes>(
            IPipelineBehavior<TReq, TRes>[] behaviors,
            TReq request,
            IRequestHandler<TReq, TRes> handler,
            Func<IRequestHandler<TReq, TRes>, TReq, CancellationToken, Task<TRes>> handlerInvoker,
            Func<IPipelineBehavior<TReq, TRes>, TReq,
                RequestHandlerDelegate<TRes>, CancellationToken, Task<TRes>> behaviorInvoker,
            CancellationToken ct)
            where TReq : IRequest<TRes>
        {
            RequestHandlerDelegate<TRes> next = () => handlerInvoker(handler, request, ct);

            for (int i = behaviors.Length - 1; i >= 0; i--)
            {
                var behavior = behaviors[i];
                var prevNext = next;
                next = () => behaviorInvoker(behavior, request, prevNext, ct);
            }

            return next();
        }

        // ─── Скомпилированные инвокеры — вызываются один раз
        private static Func<IRequestHandler<TReq, TRes>, TReq, CancellationToken, Task<TRes>>
            CreateHandlerInvoker<TReq, TRes>() where TReq : IRequest<TRes>
        {
            var h = Expression.Parameter(typeof(IRequestHandler<TReq, TRes>), "h");
            var r = Expression.Parameter(typeof(TReq), "r");
            var c = Expression.Parameter(typeof(CancellationToken), "c");
            return Expression.Lambda<
                Func<IRequestHandler<TReq, TRes>, TReq, CancellationToken, Task<TRes>>>(
                Expression.Call(h, nameof(IRequestHandler<TReq, TRes>.HandleAsync), null, r, c),
                h, r, c).Compile();
        }

        private static Func<IPipelineBehavior<TReq, TRes>, TReq,
                RequestHandlerDelegate<TRes>, CancellationToken, Task<TRes>>
            CreateBehaviorInvoker<TReq, TRes>() where TReq : IRequest<TRes>
        {
            var b = Expression.Parameter(typeof(IPipelineBehavior<TReq, TRes>), "b");
            var r = Expression.Parameter(typeof(TReq), "r");
            var n = Expression.Parameter(typeof(RequestHandlerDelegate<TRes>), "n");
            var c = Expression.Parameter(typeof(CancellationToken), "c");
            return Expression.Lambda<
                Func<IPipelineBehavior<TReq, TRes>, TReq,
                    RequestHandlerDelegate<TRes>, CancellationToken, Task<TRes>>>(
                Expression.Call(b, nameof(IPipelineBehavior<TReq, TRes>.HandleAsync), null, r, n, c),
                b, r, n, c).Compile();
        }
    }
}
