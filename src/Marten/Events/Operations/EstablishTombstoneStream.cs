using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Operations;
using Weasel.Postgresql;
using Marten.Schema;
using Marten.Storage;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Events.Operations
{
    [DocumentAlias("tombstone")]
    internal class Tombstone
    {
        public static readonly string Name = "tombstone";
    }

    internal class EstablishTombstoneStream : IStorageOperation
    {
        public static readonly string StreamKey = "mt_tombstone";
        public static readonly Guid StreamId = Guid.NewGuid();

        private string _sql;
        private readonly Action<NpgsqlParameter> _configureParameter;

        public EstablishTombstoneStream(EventGraph events)
        {
            var pkFields = events.TenancyStyle == TenancyStyle.Conjoined
                ? "id, tenant_id"
                : "id";

            _sql = $@"
insert into {events.DatabaseSchemaName}.mt_streams (id, tenant_id, version)
values (?, '*DEFAULT*', 0)
ON CONFLICT ({pkFields})
DO NOTHING
";

            if (events.StreamIdentity == StreamIdentity.AsGuid)
            {
                _configureParameter = p =>
                {
                    p.Value = StreamId;
                    p.NpgsqlDbType = NpgsqlDbType.Uuid;
                };
            }
            else
            {
                _configureParameter = p =>
                {
                    p.Value = StreamKey;
                    p.NpgsqlDbType = NpgsqlDbType.Varchar;
                };
            }

        }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            var parameters = builder.AppendWithParameters(_sql);
            _configureParameter(parameters[0]);
        }

        public Type DocumentType => typeof(IEvent);
        public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
        {
            // Nothing
        }

        public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        {
            // Nothing
            return Task.CompletedTask;
        }

        public OperationRole Role()
        {
            return OperationRole.Events;
        }
    }
}
