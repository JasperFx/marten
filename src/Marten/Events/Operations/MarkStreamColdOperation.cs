#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Schema;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Services;
using Marten.Storage;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Operations;

/// <summary>
/// Behind <c>IEventStoreOperations.MarkStreamCold</c>: deletes the stream's events through
/// <c>throughVersion</c> from <c>mt_events</c> (and their DCB tag rows), and records the boundary in
/// <c>mt_streams.cold_version</c>.
/// </summary>
/// <remarks>
/// One statement of data-modifying CTEs, for the reasons on <see cref="DeleteStreamOperation" />. It returns
/// the stream's row so a stream that does not exist, or has not reached <c>throughVersion</c>, fails the
/// unit of work instead of silently deleting nothing. The boundary only ever moves up.
/// </remarks>
internal class MarkStreamColdOperation: IStorageOperation
{
    private readonly EventGraph _events;
    private readonly object _streamId;
    private readonly long _throughVersion;
    private readonly string _tenantId;

    public MarkStreamColdOperation(EventGraph events, object streamId, long throughVersion, string tenantId)
    {
        if (throughVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(throughVersion), "A stream's cold part has at least version 1.");
        }

        _events = events;
        _streamId = streamId;
        _throughVersion = throughVersion;
        _tenantId = tenantId;
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        var conjoined = _events.TenancyStyle == TenancyStyle.Conjoined;
        var schema = _events.DatabaseSchemaName;

        var sql = new StringBuilder();
        var parameters = new List<(object Value, NpgsqlDbType Type)>();

        void streamPredicate(string column)
        {
            sql.Append(column).Append(" = ?");
            parameters.Add((_streamId, _events.StreamIdDbType));
            if (conjoined)
            {
                sql.Append(" and tenant_id = ?");
                parameters.Add((_tenantId, NpgsqlDbType.Varchar));
            }
        }

        void coldEventsPredicate()
        {
            streamPredicate("stream_id");
            sql.Append(" and version <= ?");
            parameters.Add((_throughVersion, NpgsqlDbType.Bigint));
        }

        sql.Append("with ");

        var tagTables = _events.DcbStorageMode == DcbStorageMode.HStore
            ? (IReadOnlyList<JasperFx.Events.Tags.ITagTypeRegistration>)Array.Empty<JasperFx.Events.Tags.ITagTypeRegistration>()
            : _events.TagTypes;

        for (var i = 0; i < tagTables.Count; i++)
        {
            sql.Append($"tag{i} as (delete from {schema}.mt_event_tag_{tagTables[i].TableSuffix} where ");
            if (conjoined)
            {
                sql.Append("tenant_id = ? and ");
                parameters.Add((_tenantId, NpgsqlDbType.Varchar));
            }

            sql.Append($"seq_id in (select seq_id from {schema}.mt_events where ");
            coldEventsPredicate();
            sql.Append(")), ");
        }

        sql.Append($"cold_events as (delete from {schema}.mt_events where ");
        coldEventsPredicate();
        sql.Append(')');

        sql.Append($" update {schema}.{StreamsTable.TableName} set {StreamsTable.ColdVersionColumn} = greatest(coalesce({StreamsTable.ColdVersionColumn}, 0), ?) where ");
        parameters.Add((_throughVersion, NpgsqlDbType.Bigint));
        streamPredicate("id");
        sql.Append(" and version >= ?");
        parameters.Add((_throughVersion, NpgsqlDbType.Bigint));
        sql.Append(" returning version");

        var npgsqlParameters = builder.AppendWithParameters(sql.ToString());
        for (var i = 0; i < npgsqlParameters.Length; i++)
        {
            npgsqlParameters[i].Value = parameters[i].Value;
            npgsqlParameters[i].NpgsqlDbType = parameters[i].Type;
        }
    }

    public Type DocumentType => typeof(IEvent);

    public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            exceptions.Add(new InvalidOperationException(
                $"Cannot mark stream '{_streamId}' cold through version {_throughVersion}: the stream does not exist for tenant '{_tenantId}', or has not reached that version."));
        }
    }

    public OperationRole Role() => OperationRole.Events;
}

/// <summary>
/// Behind <c>IEventStoreOperations.MarkStreamWarm</c>: clears <c>mt_streams.cold_version</c>.
/// </summary>
internal class MarkStreamWarmOperation: IStorageOperation, NoDataReturnedCall
{
    private readonly EventGraph _events;
    private readonly object _streamId;
    private readonly string _tenantId;

    public MarkStreamWarmOperation(EventGraph events, object streamId, string tenantId)
    {
        _events = events;
        _streamId = streamId;
        _tenantId = tenantId;
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        builder.Append($"update {_events.DatabaseSchemaName}.{StreamsTable.TableName} set {StreamsTable.ColdVersionColumn} = null where id = ");
        builder.AppendParameter(_streamId, _events.StreamIdDbType);

        if (_events.TenancyStyle == TenancyStyle.Conjoined)
        {
            builder.Append(" and tenant_id = ");
            builder.AppendParameter(_tenantId);
        }
    }

    public Type DocumentType => typeof(IEvent);

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public OperationRole Role() => OperationRole.Events;
}
