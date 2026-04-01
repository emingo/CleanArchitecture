using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace CleanArchitecture.Application.Common.Interfaces;

public interface IPipelinePostHandler<in TMessage, in TResponse>
    : IRegistrableCommandConstruct, IRegistrableQueryConstruct, IAsyncMessagePostHandler<TMessage, TResponse>
    where TMessage : notnull where TResponse : notnull { }
