using Marten.Events;

namespace Marten.Exceptions;

/// <summary>
/// Thrown when the StoreOptions.Events.UseMandatoryStreamTypeDeclaration setting is true and no stream type marker is set on StartStream()

/// </summary>
public class StreamTypeMissingException: MartenException
{
    public StreamTypeMissingException() : base($"A stream type declaration is required when starting a new event stream because of the {nameof(EventGraph.UseMandatoryStreamTypeDeclaration)} settings. Please use one of the {nameof(IEventStore.StartStream)} overloads that take in an stream type instead and supply the stream type (usually an aggregate type)")
    {
    }
}
