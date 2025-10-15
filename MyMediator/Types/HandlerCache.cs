using System.Collections.Concurrent;

namespace MyMediator.Types
{
    static class HandlerCache<TResponse>
    {
        private static readonly ConcurrentDictionary<Type, (Type Handler, Func<object, object, CancellationToken, Task<TResponse>> Invoke)> _cache =
            new();

        public static (Type Handler, Func<object, object, CancellationToken, Task<TResponse>> Invoke) GetOrAdd(
            Type requestType, 
            Func<(Type, Func<object, object, CancellationToken, Task<TResponse>>)> factory)
        {
            return _cache.GetOrAdd(requestType, _ => factory());
        }
    }
}
