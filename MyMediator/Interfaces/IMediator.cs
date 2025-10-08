using MyMediator.Types;

namespace MyMediator.Interfaces
{
    public interface IMediator
    {
        Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct = default);
        Task SendAsync(IRequest command, CancellationToken ct = default);
    }
}
