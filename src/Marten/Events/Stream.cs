using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Events
{
    public class Stream
    {
        public Stream(Guid id, Type aggregateType)
        {
            Id = id;
            AggregateType = aggregateType;
        }

        public Guid Id { get; }
        public Type AggregateType { get; } 

        public readonly IList<IEvent> Events = new List<IEvent>();
    }


}