using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Exceptions;
using Npgsql;

namespace Marten.Events.Projections;

// Leave public for codegen!
public abstract class AsyncEventProjectionBase: AsyncProjectionBase
{
    public override async Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams,
        CancellationToken cancellation)
    {
        var events = streams
            .SelectMany(stream => stream.Events.Select(e => (Stream: stream, Event: e)))
            .OrderBy(e => e.Event.Sequence);

        foreach (var (stream, @event) in events)
        {
            try
            {
                await ApplyEvent(operations, stream, @event, cancellation).ConfigureAwait(false);
            }
            catch (MartenCommandException)
            {
                throw;
            }
            catch (NpgsqlException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new ApplyEventException(@event, e);
            }
        }
    }

    public abstract Task ApplyEvent(IDocumentOperations operations, StreamAction streamAction, IEvent e,
        CancellationToken cancellationToken);
}
