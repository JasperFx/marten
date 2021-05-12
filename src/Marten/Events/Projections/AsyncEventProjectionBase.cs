using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Exceptions;
using Npgsql;

namespace Marten.Events.Projections
{
    // Leave public for codegen!
    public abstract class AsyncEventProjectionBase: AsyncProjectionBase
    {
        public override async Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams,
            CancellationToken cancellation)
        {
            foreach (var stream in streams)
            {
                foreach (var @event in stream.Events)
                {
                    try
                    {
                        await ApplyEvent(operations, stream, @event, cancellation);
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
        }

        public abstract Task ApplyEvent(IDocumentOperations operations, StreamAction streamAction, IEvent e,
            CancellationToken cancellationToken);

    }
}
