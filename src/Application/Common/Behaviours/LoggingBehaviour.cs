using CleanArchitecture.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Application.Common.Behaviours;

public class LoggingBehaviour<TMessage>(ILogger<TMessage> logger, IUser user, IIdentityService identityService)
    : IPipelinePreHandler<TMessage>
    where TMessage : notnull
{
    public async Task PreHandleAsync(TMessage message, CancellationToken cancellationToken = default)
    {
        var requestName = typeof(TMessage).Name;
        var userId = user.Id ?? string.Empty;
        string? userName = string.Empty;

        if (!string.IsNullOrEmpty(userId))
        {
            userName = await identityService.GetUserNameAsync(userId);
        }

        logger.LogInformation("CleanArchitecture Request: {Name} {@UserId} {@UserName} {@Request}",
            requestName, userId, userName, message);
    }
}
