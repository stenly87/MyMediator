using MyMediator.Interfaces;
using System.Linq.Expressions;
using System.Reflection;

namespace MyMediator.Types
{
    /// <summary>
    /// Основной класс медиатора. Содержит словарь соответствий типа Команда - Обработчик
    /// </summary>
    public class Mediator : IMediator
    {
        private readonly IServiceProvider _serviceProvider;

        public Mediator(IServiceProvider serviceProvider)
            => _serviceProvider = serviceProvider;

        /// <summary>
        /// Перегрузка для команд, реализующих IRequest<Unit>
        /// </summary>
        /// <param name="command">Команда</param>
        /// <param name="ct">Стандартный токен отмены</param>
        /// <returns></returns>
        public Task SendAsync(IRequest command, CancellationToken ct = default)
            => SendAsync<Unit>(command, ct);

        /// <summary>
        /// Реализация паттерна Медиатор. Вызывается хэндлер, соответствующий команде из аргумента request
        /// </summary>
        /// <typeparam name="TResponse">Тип ответа</typeparam>
        /// <param name="request">Команда</param>
        /// <param name="ct">Стандартный токен отмены</param>
        /// <returns>Результат обработки команды</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            var requestType = request.GetType();

            var method = HandlerCache<TResponse>.GetOrAdd(requestType, () =>
            {
                var concreteHandlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));
                var handleMethod = concreteHandlerType.GetMethod("HandleAsync")
                    ?? throw new InvalidOperationException($"Handler for {requestType} must implement HandleAsync.");
                var run = CreateHandlerDelegate<TResponse>(requestType, concreteHandlerType, handleMethod);
                return (concreteHandlerType, run);
            });
            
            var handler = _serviceProvider.GetService(method.Handler);
            if (handler == null)
                throw new InvalidOperationException($"Handler for {requestType} is not registered.");

            return await method.Invoke(handler, request, ct);
        }

        private static Func<object, object, CancellationToken, Task<TResponse>> CreateHandlerDelegate<TResponse>(
            Type requestType, Type handlerType, MethodInfo method)
        {
            var h = Expression.Parameter(typeof(object), "h");
            var r = Expression.Parameter(typeof(object), "r");
            var c = Expression.Parameter(typeof(CancellationToken), "c");

            var call = Expression.Call(
                Expression.Convert(h, handlerType),
                method,
                Expression.Convert(r, requestType),
                c
            );

            return Expression.Lambda<Func<object, object, CancellationToken, Task<TResponse>>>(call, h, r, c).Compile();
        }
    }
}
