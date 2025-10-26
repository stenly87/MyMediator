using MyMediator.Interfaces;
using MyMediator.Types;
using System.ComponentModel.DataAnnotations;

namespace MediatorUseSample.Behaviors
{
    public interface IValidator<TRequest> 
    {
        Task<IEnumerable<string>> ValidateAsync(TRequest request, CancellationToken ct);
    }

    public class ValidatorBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    {
        private readonly IEnumerable<IValidator<TRequest>> _validators;

        public ValidatorBehavior(IEnumerable<IValidator<TRequest>> validators)
        {
            _validators = validators;
        }

        public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
        {
            if (!_validators.Any())
                return await next();

            var failures = new List<string>();

            foreach (var validator in _validators)
            {
                var result = await validator.ValidateAsync(request, ct);
                if (result != null)
                    failures.AddRange(result);
            }

            if (failures.Any())
            {
                throw new ValidationException(string.Join(";", failures));
            }
            else
                return await next();
        }
    }
}
