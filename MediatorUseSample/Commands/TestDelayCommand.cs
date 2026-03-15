using MyMediator.Interfaces;
using MyMediator.Types;

namespace MediatorUseSample.Commands
{
    public class TestDelayCommand : IRequest<Unit>
    {
        public class TestDelayCommandHandler : IRequestHandler<TestDelayCommand, Unit>
        {
            public async Task<Unit> HandleAsync(TestDelayCommand request, CancellationToken ct = default)
            {
                await Task.Delay(500);
                return Unit.Value;
            }
        }
    }
}
