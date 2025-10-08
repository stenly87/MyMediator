using Microsoft.Extensions.DependencyInjection;
using MyMediator.Interfaces;
using System.Reflection;

namespace MyMediator.Extension
{
    public static class ServiceCollectionExtension
    {
        public static void AddMediatorHandlers(this IServiceCollection services, Assembly assembly)
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
