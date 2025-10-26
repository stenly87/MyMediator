using MyMediator.Interfaces;

namespace MediatorUseSample.Commands
{
    public class TestCalcCommand : IRequest<int>
    {
        public int X { get; set; }
        public int Y { get; set; }

        public class TestCalsCommandHandler : IRequestHandler<TestCalcCommand, int>
        {
            public async Task<int> HandleAsync(TestCalcCommand request, CancellationToken ct = default)
            {
                return request.X + request.Y;
            }
        }
    }
}
