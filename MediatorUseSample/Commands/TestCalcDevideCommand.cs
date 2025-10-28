using MediatorUseSample.ExceptionHandler;
using MyMediator.Interfaces;

namespace MediatorUseSample.Commands
{
    public class TestCalcDevideCommand : IRequest<double>
    {
        public double X { get; set; }
        public double Y { get; set; }

        public class TestCalsDevideCommandHandler : IRequestHandler<TestCalcDevideCommand, double>
        {
            public async Task<double> HandleAsync(TestCalcDevideCommand request, CancellationToken ct = default)
            {
                if (request.Y == 0)
                    throw new CustomException { ErrorCode = 1000, ErrorMessage = "У нас не принято делить на 0!" };

                return request.X / request.Y;
            }
        }
    }

    
}
