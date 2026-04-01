using System.Diagnostics;
using CleanArchitecture.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Application.Common.Behaviours;

public class PerformanceContext
{
    public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();
}

public class PerformanceBehaviour<TRequest, TResponse>(
    PerformanceContext context, ILogger<TRequest> logger, IUser user, IIdentityService identityService)
    : IPipelinePostHandler<TRequest, TResponse>
    where TRequest : notnull where TResponse : notnull
{
    public async Task PostHandleAsync(TRequest message, TResponse? messageResult, CancellationToken cancellationToken = default)
    {
        context.Stopwatch.Stop();

        var elapsedMilliseconds = context.Stopwatch.ElapsedMilliseconds;

        if (elapsedMilliseconds > 500)
        {
            var requestName = typeof(TRequest).Name;
            var userId = user.Id ?? string.Empty;
            var userName = string.Empty;

            if (!string.IsNullOrEmpty(userId))
            {
                userName = await identityService.GetUserNameAsync(userId);
            }

            logger.LogWarning("CleanArchitecture Long Running Request: {Name} ({ElapsedMilliseconds} milliseconds) {@UserId} {@UserName} {@Request}",
                requestName, elapsedMilliseconds, userId, userName, message);
        }
    }
}
