using System;
using System.Collections.Generic;
using Marten.Exceptions;
using Npgsql;

namespace Marten.Events.Projections
{
    /// <summary>
    /// Base class for event projections that are strictly synchronous
    /// </summary>
    public abstract class SyncEventProjectionBase: SyncProjectionBase
    {
        public override void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
        {
            foreach (var stream in streams)
            {
                foreach (var @event in stream.Events)
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
        }

        public abstract void ApplyEvent(IDocumentOperations operations, StreamAction streamAction, IEvent e);
    }
}
