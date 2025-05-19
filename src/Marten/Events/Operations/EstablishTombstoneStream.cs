using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Schema;
using Marten.Storage;
using Npgsql;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Operations;

internal class EstablishTombstoneStream: IStorageOperation
{

    private readonly Action<NpgsqlParameter> _configureParameter;
    private readonly string _sessionTenantId;

    private readonly string _sql;

    public EstablishTombstoneStream(EventGraph events, string sessionTenantId)
    {
        _sessionTenantId = sessionTenantId;
        var pkFields = "id";
        if (events.TenancyStyle == TenancyStyle.Conjoined)
        {
            pkFields += ", tenant_id";
        }
        if (events.UseArchivedStreamPartitioning)
        {
            pkFields += ", is_archived";
        }

        _sql = $@"
insert into {events.DatabaseSchemaName}.mt_streams (id, tenant_id, version, is_archived)
values (?, ?, 0, false)
ON CONFLICT ({pkFields})
DO NOTHING
";

        if (events.StreamIdentity == StreamIdentity.AsGuid)
        {
            _configureParameter = p =>
            {
                p.Value = Tombstone.StreamId;
                p.NpgsqlDbType = NpgsqlDbType.Uuid;
            };
        }
        else
        {
            _configureParameter = p =>
            {
                p.Value = Tombstone.StreamKey;
                p.NpgsqlDbType = NpgsqlDbType.Varchar;
            };
        }
    }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        var parameters = builder.AppendWithParameters(_sql);
        _configureParameter(parameters[0]);
        parameters[1].Value = _sessionTenantId;
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
