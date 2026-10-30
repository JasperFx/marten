#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using Marten.Events.Cold;
using Marten.Exceptions;

namespace Marten.Events;

public partial class EventGraph
{
    /// <summary>
    ///     Where the cold part of event streams is read from, or null when no stream can be cold.
    /// </summary>
    public IColdEventStore? ColdEventStore { get; private set; }

    public IEventStoreOptions UseColdEventStore(IColdEventStore coldEventStore)
    {
        ColdEventStore = coldEventStore ?? throw new ArgumentNullException(nameof(coldEventStore));
        return this;
    }

    public IEventStoreOptions UseColdEventStore<T>() where T : IColdEventStore, new()
    {
        return UseColdEventStore(new T());
    }

    /// <summary>
    ///     Turn an event stored outside <c>mt_events</c> into an <see cref="IEvent" />, resolving its type and
    ///     running its upcasters exactly as for an event read from <c>mt_events</c>.
    /// </summary>
    /// <exception cref="UnknownEventTypeException">Neither the event type name nor the .NET type name is known.</exception>
    /// <exception cref="EventDeserializationFailureException">The payload could not be deserialized.</exception>
    public async ValueTask<IEvent> DeserializeAsync(ColdEvent stored, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(stored);

        var mapping = storedEventMapping(stored);

        IEvent @event;
        try
        {
            if (stored.BinaryData != null)
            {
                if (mapping.BinarySerializer is null)
                {
                    throw new InvalidOperationException(
                        $"Event {stored.Id} has a binary payload, but no IEventBinarySerializer is registered for type '{mapping.DocumentType.FullName}'.");
                }

                @event = mapping.Wrap(mapping.BinarySerializer.Deserialize(mapping.DocumentType, stored.BinaryData));
            }
            else
            {
                if (stored.Data == null)
                {
                    throw new ArgumentException($"Event {stored.Id} has neither {nameof(ColdEvent.Data)} nor {nameof(ColdEvent.BinaryData)}.", nameof(stored));
                }

                await using var reader = new JsonPayloadReader(stored.Data);
                await reader.ReadAsync(token).ConfigureAwait(false);
                @event = await mapping.ReadEventDataAsync(Options.Serializer(), reader, token).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is not ArgumentException and not OperationCanceledException)
        {
            throw new EventDeserializationFailureException(stored.Sequence, mapping, e);
        }

        @event.Id = stored.Id;
        @event.Version = stored.Version;
        @event.Sequence = stored.Sequence;
        @event.Timestamp = stored.Timestamp;
        @event.StreamKey = stored.StreamKey;
        @event.TenantId = stored.TenantId ?? StorageConstants.DefaultTenantId;
        @event.CorrelationId = stored.CorrelationId;
        @event.CausationId = stored.CausationId;
        @event.UserName = stored.UserName;

        if (stored.Headers != null)
        {
            foreach (var header in stored.Headers)
            {
                @event.SetHeader(header.Key, header.Value);
            }
        }

        if (stored.Tags != null)
        {
            foreach (var tag in stored.Tags)
            {
                @event.AddTag(tag);
            }
        }

        return @event;
    }

    private EventMapping storedEventMapping(ColdEvent stored)
    {
        var mapping = EventMappingFor(stored.EventTypeName);
        if (mapping == null)
        {
            return EventMappingForDotNetTypeName(stored.DotNetTypeName, stored.EventTypeName, stored.Sequence);
        }

        // Same rule as a read from mt_events (#4680): an upcaster owns its source name, otherwise the
        // stored .NET type picks between CLR types sharing one event type name.
        if (!mapping.IsUpcastTarget && stored.DotNetTypeName.IsNotEmpty() && stored.DotNetTypeName != mapping.DotNetTypeName)
        {
            return TryGetRegisteredMappingForDotNetTypeName(stored.DotNetTypeName!) ?? mapping;
        }

        return mapping;
    }

    /// <summary>
    ///     The fallback for a stored event type name the store does not know: the stored .NET type name.
    ///     <paramref name="sequence" /> only names the event in the exception (#5048 / jasperfx#565).
    /// </summary>
    internal EventMapping EventMappingForDotNetTypeName(string? dotnetTypeName, string eventTypeName, long sequence)
    {
        if (dotnetTypeName.IsEmpty())
        {
            throw new UnknownEventTypeException(eventTypeName, sequence);
        }

        Type type;
        try
        {
            type = TypeForDotNetName(dotnetTypeName!);
        }
        catch (ArgumentNullException)
        {
            throw new UnknownEventTypeException(dotnetTypeName!, sequence);
        }

        return EventMappingFor(type);
    }
}
