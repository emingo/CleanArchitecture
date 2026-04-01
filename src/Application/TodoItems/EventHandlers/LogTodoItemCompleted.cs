using CleanArchitecture.Domain.Events;
using LiteBus.Events.Abstractions;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Application.TodoItems.EventHandlers;

public class LogTodoItemCompleted : IEventHandler<TodoItemCompletedEvent>
{
    private readonly ILogger<LogTodoItemCompleted> _logger;

    public LogTodoItemCompleted(ILogger<LogTodoItemCompleted> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(TodoItemCompletedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("CleanArchitecture Domain Event: {DomainEvent}", @event.GetType().Name);

        return Task.CompletedTask;
    }
}
