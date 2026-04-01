using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.ValueObjects;

namespace CleanArchitecture.Application.TodoLists.Commands.UpdateTodoList;

public record UpdateTodoListCommand : ICommand
{
    public int Id { get; init; }

    public string? Title { get; init; }

    public string? Colour { get; init; }
}

public class UpdateTodoListCommandHandler : ICommandHandler<UpdateTodoListCommand>
{
    private readonly IApplicationDbContext _context;

    public UpdateTodoListCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task HandleAsync(UpdateTodoListCommand request, CancellationToken cancellationToken = default)
    {
        var entity = await _context.TodoLists
            .FindAsync([request.Id], cancellationToken);

        Guard.Against.NotFound(request.Id, entity);

        entity.Title = request.Title;

        if (request.Colour is not null)
        {
            entity.Colour = Colour.From(request.Colour);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
