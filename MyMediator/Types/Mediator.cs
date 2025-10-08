using Microsoft.Extensions.DependencyInjection;
using MyMediator.Interfaces;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace MyMediator.Types
{
    public class Mediator : IMediator
    {
        private readonly IServiceProvider _serviceProvider;

        public Mediator(IServiceProvider serviceProvider)
            => _serviceProvider = serviceProvider;

        private static readonly ConcurrentDictionary<Type, (Type, MethodInfo)> _handleMethods = new();

        public Task SendAsync(IRequest command, CancellationToken ct = default)
            => SendAsync<Unit>(command, ct);

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

            var result = await (Task<TResponse>)method.Invoker.Invoke(handler, new object[] { request, ct })!;
            return result;
        }

        public static void RegisterHandlers(IServiceCollection services, Assembly assembly)
        {
            foreach (var type in assembly.GetTypes())
            {
                foreach (var iface in type.GetInterfaces()
                             .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)))
                {
                    services.AddTransient(iface, type);
                }
            }
        }

        
    }
}
