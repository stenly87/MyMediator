﻿namespace MyMediator.Interfaces
{
    public interface IRequestHandler<in TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        Task<TResponse> HandleAsync(TRequest request, CancellationToken ct = default);
    }
}
