using ValidationException = CleanArchitecture.Application.Common.Exceptions.ValidationException;

namespace CleanArchitecture.Application.Common.Behaviours;

public class ValidationBehaviour<TRequest>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelinePreHandler<TRequest>
    where TRequest : notnull, ICommand
{
    public async Task PreHandleAsync(TRequest message, CancellationToken cancellationToken = default)
    {
        if (!validators.Any()) return;

        var validationResults = await Task.WhenAll(
            validators.Select(v =>
                v.ValidateAsync(new ValidationContext<TRequest>(message), cancellationToken)));

        var failures = validationResults
            .Where(r => r.Errors.Any())
            .SelectMany(r => r.Errors)
            .ToList();

        if (failures.Count != 0)
            throw new ValidationException(failures);
    }
}
