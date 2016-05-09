using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.QueryHandlers;
using Marten.Services;
using Npgsql;

namespace Marten.Events.Projections.Async
{
    internal class QueuedEventHandler : ListQueryHandler<IEvent>
    {
        private readonly EventGraph _events;

        public QueuedEventHandler(EventGraph graph, ISerializer serializer)
            : base(new EventSelector(graph, serializer))
        {
            _events = graph;
        }

        public override void ConfigureCommand(NpgsqlCommand command)
        {
            // Add Stream to event selector
            var sql = $"select id, type, version, data from {_events.DatabaseSchemaName}.mt_events";
        }

        public override Type SourceType => typeof(IEvent);
    }
}