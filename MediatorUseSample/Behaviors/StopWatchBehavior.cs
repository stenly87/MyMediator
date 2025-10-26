using MyMediator.Interfaces;
using MyMediator.Types;

namespace MediatorUseSample.Behaviors
{
    public class StopWatchBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    {
        public async Task<TResponse> HandleAsync(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken ct)
        {
            var requestName = typeof(TRequest).Name;
            Console.WriteLine($"[START] обработка команды {requestName}");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var response = await next(); // Выполняем команду
                Console.WriteLine($"[SUCCESS]");
                return response;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] {ex.Message}");
                throw;
            }
            finally
            {
                stopwatch.Stop();
                Console.WriteLine($"[TIME] {requestName} время работы: {stopwatch.ElapsedMilliseconds} ms");
                Console.WriteLine($"[END] дообрабатывали команду {requestName}");
                Console.WriteLine();
            }
        }
    }
}
