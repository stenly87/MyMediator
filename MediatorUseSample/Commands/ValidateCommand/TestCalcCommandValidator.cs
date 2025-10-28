using MediatorUseSample.Behaviors;

namespace MediatorUseSample.Commands.ValidateCommand
{
    public class TestCalcCommandValidator : IValidator<TestCalcCommand>
    {
        public async Task<IEnumerable<string>> ValidateAsync(TestCalcCommand request, CancellationToken ct)
        {
            var result = new List<string>();
            if (request.X > 0 && request.Y > 0 &&request.X + request.Y < 0)
                result.Add("Слишком большие числа");

            return result;
        }
    }
}
