using MyMediator.Interfaces;
using System.Collections.Concurrent;
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

        private static readonly ConcurrentDictionary<Type, (Type, MethodInfo)> _handleMethods = new();

        /// <summary>
        /// Перегрузка для команд, реализующих IRequest<Unit>
        /// </summary>
        /// <param name="command">Команда</param>
        /// <param name="ct">Стандартный токен отмены</param>
        /// <returns></returns>
        public Task SendAsync(IRequest command, CancellationToken ct = default)
            => SendAsync<Unit>(command, ct);

        /// <summary>
        /// Реализация паттера Медиатор. Вызывается хэндлер, соответствующий команде из аргумента request
        /// </summary>
        /// <typeparam name="TResponse">Тип ответа</typeparam>
        /// <param name="request">Команда</param>
        /// <param name="ct">Стандартный токен отмены</param>
        /// <returns>Результат обработки команды</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            var requestType = request.GetType();
            
            (Type ServiceType, MethodInfo Invoker) method = _handleMethods.GetOrAdd(requestType, t =>
            {
                var responseType = typeof(TResponse);
                var concreteHandlerType = typeof(IRequestHandler<,>).MakeGenericType(t, responseType);
                var handleMethod = concreteHandlerType.GetMethod("HandleAsync")
                    ?? throw new InvalidOperationException($"Handler for {t} must implement HandleAsync.");

                return (concreteHandlerType, handleMethod);
            });
            
            var handler = _serviceProvider.GetService(method.ServiceType);
            if (handler == null)
                throw new InvalidOperationException($"Handler for {requestType} is not registered.");

            var result = await (Task<TResponse>)method.Invoker.Invoke(handler, [request, ct])!;
            return result;
        }        
    }
}
