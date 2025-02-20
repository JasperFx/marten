using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Events;
using Marten.Exceptions;
using Npgsql;

namespace Marten.Events.Projections;

/// <summary>
///     Base class for event projections that are strictly synchronous
/// </summary>
public abstract class SyncEventProjectionBase: SyncProjectionBase
{
    public override void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
    {
        var events = streams
            .SelectMany(stream => stream.Events.Select(e => (Stream: stream, Event: e)))
            .OrderBy(e => e.Event.Sequence);

        foreach (var (stream, @event) in events)
        {
            try
            {
                ApplyEvent(operations, stream, @event);
            }
            catch (NpgsqlException)
            {
                throw;
            }
            catch (MartenCommandException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ApplyEventException(@event, ex);
            }
        }
    }

    public abstract void ApplyEvent(IDocumentOperations operations, StreamAction streamAction, IEvent e);
}
