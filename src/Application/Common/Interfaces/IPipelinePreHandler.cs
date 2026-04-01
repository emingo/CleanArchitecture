using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace CleanArchitecture.Application.Common.Interfaces;

public interface IPipelinePreHandler<in TMessage>
    : IRegistrableCommandConstruct, IRegistrableQueryConstruct, IAsyncMessagePreHandler<TMessage>
    where TMessage : notnull { }
