using System.Runtime.ExceptionServices;
using LiteBus.Commands.Abstractions;
using LiteBus.Queries.Abstractions;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Application.Common.Behaviours;

public sealed class UnhandledCommandExceptionHandler(ILogger<UnhandledCommandExceptionHandler> logger)
    : ICommandErrorHandler
{
    public Task HandleErrorAsync(ICommand message, object? messageResult, Exception exception, CancellationToken cancellationToken = default)
    {
        ExceptionLogging.LogAndRethrow(logger, message, exception);
        return Task.CompletedTask; // unreachable
    }
}

public sealed class UnhandledQueryExceptionHandler(ILogger<UnhandledQueryExceptionHandler> logger)
    : IQueryErrorHandler
{
    public Task HandleErrorAsync(IQuery message, object? messageResult, Exception exception, CancellationToken cancellationToken = default)
    {
        ExceptionLogging.LogAndRethrow(logger, message, exception);
        return Task.CompletedTask; // unreachable
    }
}

internal static class ExceptionLogging
{
    public static void LogAndRethrow(ILogger logger, object message, Exception exception)
    {
        var requestName = message.GetType().Name;
        logger.LogError(exception, "CleanArchitecture Request: Unhandled Exception for Request {Name} {@Request}", requestName, message);
        ExceptionDispatchInfo.Capture(exception).Throw();
    }
}
