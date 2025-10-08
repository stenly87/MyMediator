using MyMediator.Types;

namespace MyMediator.Interfaces
{
    public interface IRequest<out TResponse> { }

    public interface IRequest : IRequest<Unit> { }
}
