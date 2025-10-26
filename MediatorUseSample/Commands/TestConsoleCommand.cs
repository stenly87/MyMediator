using MyMediator.Interfaces;
using MyMediator.Types;

namespace MediatorUseSample.Commands
{
    public class TestConsoleCommand : IRequest
    {
        public class TestConsoleCommandHandler : IRequestHandler<TestConsoleCommand, Unit>
        {
            public async Task<Unit> HandleAsync(TestConsoleCommand request, CancellationToken ct = default)
            {
                Console.WriteLine("Привет из консоли. Я TestConsoleCommand");
                return Unit.Value;
            }
        }
    }
}
