# Migrating from MediatR to LiteBus

Migration guide for the [Jason Taylor Clean Architecture template](https://github.com/jasontaylordev/CleanArchitecture) — replacing MediatR with [LiteBus](https://github.com/litenova/LiteBus).

**Source template version:** .NET 8/9/10 with MediatR 14.x
**Target:** LiteBus 4.3.0

## Why LiteBus

- MIT license (MediatR moved to a commercial license)
- Semantic CQRS: separate `ICommand`, `IQuery`, `IEvent` types instead of generic `IRequest<T>`
- Linear pipeline with distinct phases (pre → handler → post → error) vs MediatR's nested "Russian doll" behaviors
- Built-in assembly scanning for handler/behaviour discovery

## Key Architectural Differences

| Aspect | MediatR | LiteBus |
|--------|---------|---------|
| Pipeline model | Nested behaviours (each wraps `next()`) | Linear phases: pre-handlers → handler → post-handlers → error-handlers |
| Message types | `IRequest<T>` for everything | `ICommand<T>`, `IQuery<T>`, `IEvent` |
| Mediator | Single `IMediator` / `ISender` | Separate `ICommandMediator`, `IQueryMediator`, `IEventMediator` |
| Events | `INotification` + `INotificationHandler<T>` | `IEvent` + `IEventHandler<T>` |
| Pre-processing | `IRequestPreProcessor<T>` | `IAsyncMessagePreHandler<T>` |
| Error handling | Try/catch in a behaviour wrapping `next()` | Dedicated error handler phase; **errors are swallowed by default** if a handler exists |
| Handler discovery | `RegisterServicesFromAssembly()` | `RegisterFromAssembly()` per module (command/query/event) |
| Behaviour registration | `AddOpenBehavior()` in MediatR config | Auto-discovered via `IRegistrableCommandConstruct` / `IRegistrableQueryConstruct` markers |

## Migration Steps

### 1. Package Changes

**Remove** from `Directory.Packages.props`:
```xml
<PackageVersion Include="MediatR" Version="..." />
```

**Add:**
```xml
<PackageVersion Include="LiteBus" Version="4.3.0" />
<PackageVersion Include="LiteBus.Extensions.Microsoft.DependencyInjection" Version="4.3.0" />
```

**Project references:**

| Project | Remove | Add |
|---------|--------|-----|
| Domain.csproj | `MediatR.Contracts` (if separate) | `LiteBus` |
| Application.csproj | `MediatR` | `LiteBus` + `LiteBus.Extensions.Microsoft.DependencyInjection` |
| Infrastructure.csproj | — (MediatR resolved transitively) | — (LiteBus resolved transitively) |

### 2. Domain Layer

**`Domain/Common/BaseEvent.cs`** — change the marker interface:

```csharp
// Before
using MediatR;
public abstract class BaseEvent : INotification { }

// After
using LiteBus.Events.Abstractions;
public abstract class BaseEvent : IEvent { }
```

### 3. Application Layer — Global Usings

**`Application/GlobalUsings.cs`:**

```csharp
// Remove
global using MediatR;

// Add
global using LiteBus.Commands.Abstractions;
global using LiteBus.Queries.Abstractions;
global using LiteBus.Messaging.Abstractions;
```

### 4. Application Layer — Pipeline Adapter Interfaces

LiteBus has separate command/query modules. A behaviour that should run for *both* commands and queries needs to implement markers from both modules. Create adapter interfaces to avoid repeating this on every behaviour.

**`Application/Common/Interfaces/IPipelinePreHandler.cs`:**
```csharp
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace YourApp.Application.Common.Interfaces;

public interface IPipelinePreHandler<in TMessage>
    : IRegistrableCommandConstruct, IRegistrableQueryConstruct, IAsyncMessagePreHandler<TMessage>
    where TMessage : notnull { }
```

**`Application/Common/Interfaces/IPipelinePostHandler.cs`:**
```csharp
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace YourApp.Application.Common.Interfaces;

public interface IPipelinePostHandler<in TMessage, in TResponse>
    : IRegistrableCommandConstruct, IRegistrableQueryConstruct, IAsyncMessagePostHandler<TMessage, TResponse>
    where TMessage : notnull where TResponse : notnull { }
```

No custom adapter is needed for error handlers — use LiteBus's built-in `ICommandErrorHandler` and `IQueryErrorHandler` directly.

### 5. Application Layer — Behaviour Migration

#### 5a. LoggingBehaviour (pre-processor → pre-handler)

```csharp
// Before: MediatR
public class LoggingBehaviour<TRequest> : IRequestPreProcessor<TRequest>
    where TRequest : notnull
{
    public async Task Process(TRequest request, CancellationToken cancellationToken) { ... }
}

// After: LiteBus
public class LoggingBehaviour<TMessage> : IPipelinePreHandler<TMessage>
    where TMessage : notnull
{
    public async Task PreHandleAsync(TMessage message, CancellationToken cancellationToken = default) { ... }
}
```

Changes:
- `IRequestPreProcessor<T>` → `IPipelinePreHandler<T>` (the adapter interface from step 4)
- `Process(T request, CancellationToken)` → `PreHandleAsync(T message, CancellationToken = default)`
- Remove `using MediatR.Pipeline;`

#### 5b. AuthorizationBehaviour (behaviour → pre-handler)

```csharp
// Before: MediatR
public class AuthorizationBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        // ... authorization checks ...
        return await next();
    }
}

// After: LiteBus
public class AuthorizationBehaviour<TMessage>(IUser user, IIdentityService identityService)
    : IPipelinePreHandler<TMessage> where TMessage : notnull
{
    public async Task PreHandleAsync(TMessage message, CancellationToken cancellationToken = default)
    {
        // ... same authorization checks, just throw on failure ...
        // No need to call next() — pre-handlers run before the handler automatically
    }
}
```

Changes:
- `IPipelineBehavior<TRequest, TResponse>` → `IPipelinePreHandler<TMessage>` (drop `TResponse`)
- `Handle(TRequest, RequestHandlerDelegate<TResponse>, CancellationToken)` → `PreHandleAsync(TMessage, CancellationToken)`
- Remove `return await next();` — pre-handlers don't control the pipeline; they just throw to abort

#### 5c. ValidationBehaviour (behaviour → pre-handler, narrowed to commands)

```csharp
// Before: MediatR — validates all requests
public class ValidationBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        // ... validate ... 
        return await next();
    }
}

// After: LiteBus — validates commands only
public class ValidationBehaviour<TRequest>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelinePreHandler<TRequest> where TRequest : notnull, ICommand
{
    public async Task PreHandleAsync(TRequest message, CancellationToken cancellationToken = default)
    {
        if (!validators.Any()) return;
        // ... same validation logic, throw ValidationException on failure ...
    }
}
```

Changes:
- `IPipelineBehavior<TRequest, TResponse>` → `IPipelinePreHandler<TRequest>`
- Added `ICommand` constraint — queries typically don't need validation. Remove this constraint if you need to validate queries too.
- Remove `return await next();`

#### 5d. PerformanceBehaviour (wrapping behaviour → post-handler + scoped context)

MediatR's nested pipeline lets a single behaviour start a timer, call `next()`, then stop the timer. LiteBus's linear pipeline requires splitting this into separate phases.

```csharp
// Before: MediatR — single wrapping behaviour
public class PerformanceBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    private readonly Stopwatch _timer = new();
    
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        _timer.Start();
        var response = await next();
        _timer.Stop();
        if (_timer.ElapsedMilliseconds > 500) { /* log warning */ }
        return response;
    }
}

// After: LiteBus — scoped context + post-handler
public class PerformanceBehaviour<TRequest, TResponse>(
    PerformanceContext context, ILogger<TRequest> logger, IUser user, IIdentityService identityService)
    : IPipelinePostHandler<TRequest, TResponse>
    where TRequest : notnull where TResponse : notnull
{
    public async Task PostHandleAsync(TRequest message, TResponse? messageResult, CancellationToken cancellationToken = default)
    {
        context.Stopwatch.Stop();
        if (context.Stopwatch.ElapsedMilliseconds > 500) { /* log warning */ }
    }
}

// Scoped context — starts timing when DI scope resolves it
public class PerformanceContext
{
    public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();
}
```

Changes:
- Single `IPipelineBehavior` → `IPipelinePostHandler` (runs after the handler)
- Timer state moved to a `Scoped` `PerformanceContext` (register via `AddScoped<PerformanceContext>()`)
- Timing is broader than original — includes pre-handler execution, not just the handler. This is usually acceptable for the 500ms warning threshold.

#### 5e. UnhandledExceptionBehaviour (wrapping behaviour → error handlers)

**This is the most important migration.** LiteBus error handlers **swallow exceptions by default** — if any error handler is registered, `RunAsyncErrorHandlers` calls it and returns normally. The exception is only re-thrown when there are *zero* error handlers.

Use LiteBus's built-in `ICommandErrorHandler` + `IQueryErrorHandler` and **re-throw after logging**:

```csharp
// Before: MediatR
public class UnhandledExceptionBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        try { return await next(); }
        catch (Exception ex) { _logger.LogError(ex, "..."); throw; }
    }
}

// After: LiteBus — two handlers (commands + queries)
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
        logger.LogError(exception, "Unhandled Exception for Request {Name} {@Request}", requestName, message);
        ExceptionDispatchInfo.Capture(exception).Throw();
    }
}
```

`ICommandErrorHandler` and `IQueryErrorHandler` already carry the `IRegistrableCommandConstruct` / `IRegistrableQueryConstruct` markers, so no custom adapter interface is needed — they're discovered by `RegisterFromAssembly` automatically.

**Critical:** `ExceptionDispatchInfo.Capture(exception).Throw()` preserves the original stack trace. A plain `throw exception;` would reset it.

### 6. Application Layer — Handler Pattern

#### Commands

```csharp
// Before
public record CreateItemCommand(string Title) : IRequest<int>;
public class CreateItemCommandHandler : IRequestHandler<CreateItemCommand, int>
{
    public async Task<int> Handle(CreateItemCommand request, CancellationToken cancellationToken) { ... }
}

// After
public record CreateItemCommand(string Title) : ICommand<int>;
public class CreateItemCommandHandler : ICommandHandler<CreateItemCommand, int>
{
    public async Task<int> HandleAsync(CreateItemCommand request, CancellationToken cancellationToken = default) { ... }
}
```

For void commands: `IRequest` → `ICommand`, `IRequestHandler<T>` → `ICommandHandler<T>`.

#### Queries

```csharp
// Before
public record GetItemsQuery : IRequest<List<ItemDto>>;
public class GetItemsQueryHandler : IRequestHandler<GetItemsQuery, List<ItemDto>> { ... }

// After
public record GetItemsQuery : IQuery<List<ItemDto>>;
public class GetItemsQueryHandler : IQueryHandler<GetItemsQuery, List<ItemDto>>
{
    public async Task<List<ItemDto>> HandleAsync(GetItemsQuery request, CancellationToken cancellationToken = default) { ... }
}
```

#### Domain Event Handlers

```csharp
// Before
public class ItemCreatedHandler : INotificationHandler<ItemCreatedEvent>
{
    public async Task Handle(ItemCreatedEvent notification, CancellationToken cancellationToken) { ... }
}

// After
public class ItemCreatedHandler : IEventHandler<ItemCreatedEvent>
{
    public async Task HandleAsync(ItemCreatedEvent @event, CancellationToken cancellationToken = default) { ... }
}
```

### 7. Application Layer — DI Registration

```csharp
// Before
builder.Services.AddMediatR(cfg => {
    cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
    cfg.AddOpenRequestPreProcessor(typeof(LoggingBehaviour<>));
    cfg.AddOpenBehavior(typeof(UnhandledExceptionBehaviour<,>));
    cfg.AddOpenBehavior(typeof(AuthorizationBehaviour<,>));
    cfg.AddOpenBehavior(typeof(ValidationBehaviour<,>));
    cfg.AddOpenBehavior(typeof(PerformanceBehaviour<,>));
});

// After
builder.Services.AddScoped<PerformanceContext>();

builder.Services.AddLiteBus(liteBus =>
{
    var asm = Assembly.GetExecutingAssembly();
    liteBus.AddCommandModule(m => m.RegisterFromAssembly(asm));
    liteBus.AddQueryModule(m => m.RegisterFromAssembly(asm));
    liteBus.AddEventModule(m => m.RegisterFromAssembly(asm));
});
```

All behaviours and handlers are discovered automatically by `RegisterFromAssembly` — no manual registration needed. The adapter interfaces (`IPipelinePreHandler`, `IPipelinePostHandler`) carry the `IRegistrableCommandConstruct` / `IRegistrableQueryConstruct` markers that make assembly scanning work. The built-in `ICommandErrorHandler` / `IQueryErrorHandler` carry their own markers.

`PerformanceContext` still needs explicit registration because it's a plain class, not a LiteBus construct.

### 8. Infrastructure Layer — Domain Event Interceptor

**`Infrastructure/Data/Interceptors/DispatchDomainEventsInterceptor.cs`:**

```csharp
// Before
using MediatR;

public class DispatchDomainEventsInterceptor(IMediator mediator) : SaveChangesInterceptor
{
    private async Task DispatchDomainEvents(DbContext? context)
    {
        // ... collect domain events ...
        foreach (var domainEvent in domainEvents)
            await mediator.Publish(domainEvent);
    }
}

// After
using LiteBus.Events.Abstractions;

public class DispatchDomainEventsInterceptor(IEventMediator eventMediator) : SaveChangesInterceptor
{
    private async Task DispatchDomainEvents(DbContext? context)
    {
        // ... collect domain events (same logic) ...
        foreach (var domainEvent in domainEvents)
            await eventMediator.PublishAsync(domainEvent);
    }
}
```

Changes:
- `IMediator` → `IEventMediator`
- `mediator.Publish(domainEvent)` → `eventMediator.PublishAsync(domainEvent)`

### 9. Web Layer — Endpoint Mediator Injection

```csharp
// Before — single ISender for everything
app.MapPost("/api/items", async (CreateItemCommand cmd, ISender sender) =>
    await sender.Send(cmd));

// After — semantic mediators
app.MapPost("/api/items", async (CreateItemCommand cmd, ICommandMediator mediator) =>
    await mediator.SendAsync(cmd));

app.MapGet("/api/items", async (IQueryMediator mediator) =>
    await mediator.QueryAsync(new GetItemsQuery()));
```

## Behavioral Differences to Be Aware Of

### Pipeline execution order

MediatR (nested, inside-out):
```
Request → LoggingPreProcessor → UnhandledException wraps {
  Authorization wraps {
    Validation wraps {
      Performance wraps {
        Handler
      }
    }
  }
} → Response
```

LiteBus (linear phases):
```
Request → PreHandlers [Validation, Authorization, Logging]
        → Handler
        → PostHandlers [Performance]
        → ErrorHandlers [UnhandledCommand/QueryExceptionHandler] (only on exception)
        → Response
```

Notable differences:
- Logging now runs *after* validation/authorization (failed requests aren't logged)
- Exception handling is no longer a wrapper — it's a dedicated phase
- Pre-handler order depends on registration/discovery order

### Error handlers swallow by default

This is the biggest gotcha. In MediatR, a try/catch behaviour re-throws naturally. In LiteBus, if *any* error handler is registered, the exception is consumed and the mediator returns `default(TResult)`. **Always re-throw in error handlers** unless you genuinely want to swallow the error and return a fallback.

### Validation narrowed to commands

The template's `ValidationBehaviour` only validates `ICommand` messages, not queries. This is a deliberate design choice — queries are idempotent reads that rarely need input validation. If you need query validation, remove the `ICommand` constraint or create a separate `QueryValidationBehaviour`.

## Verification Checklist

- [ ] `dotnet build` succeeds with zero warnings
- [ ] All existing tests pass
- [ ] Trigger an unhandled exception in a handler → verify HTTP 500 (not silent null/empty)
- [ ] Trigger a validation error on a command → verify HTTP 400 with validation details
- [ ] Trigger an authorization failure → verify HTTP 401/403
- [ ] Check logs show request entries (LoggingBehaviour)
- [ ] Simulate a slow handler (>500ms) → verify performance warning in logs
