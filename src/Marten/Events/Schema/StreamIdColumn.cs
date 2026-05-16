using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Events;
using Marten.Internal.CodeGeneration;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema;

internal class StreamIdColumn: TableColumn, IEventTableColumn
{
    private readonly StreamIdentity _streamIdentity;
    private readonly Lazy<Action<DbDataReader, int, IEvent>> _readSync;
    private readonly Lazy<Func<DbDataReader, int, IEvent, CancellationToken, Task>> _readAsync;

    public StreamIdColumn(EventGraph graph): base("stream_id", "varchar")
    {
        Type = graph.GetStreamIdDBType();
        _streamIdentity = graph.StreamIdentity;

        // Build compiled-delegate readers matching the codegen path's
        // selector dispatch — Guid streams write to x.StreamId, string
        // streams write to x.StreamKey. Lazy so columns that are never
        // exercised on the closed-shape read path don't pay the
        // expression-compilation cost.
        _readSync = new Lazy<Action<DbDataReader, int, IEvent>>(() =>
            _streamIdentity == StreamIdentity.AsGuid
                ? EventColumnReaders.BuildSync(x => x.StreamId)
                : EventColumnReaders.BuildSync(x => x.StreamKey));

        _readAsync = new Lazy<Func<DbDataReader, int, IEvent, CancellationToken, Task>>(() =>
            _streamIdentity == StreamIdentity.AsGuid
                ? EventColumnReaders.BuildAsync(x => x.StreamId)
                : EventColumnReaders.BuildAsync(x => x.StreamKey));
    }

    public void GenerateSelectorCodeSync(GeneratedMethod method, EventGraph graph, int index)
    {
        if (graph.StreamIdentity == StreamIdentity.AsGuid)
        {
            method.AssignMemberFromReader<IEvent>(null, index, x => x.StreamId);
        }
        else
        {
            method.AssignMemberFromReader<IEvent>(null, index, x => x.StreamKey);
        }
    }

    public void GenerateSelectorCodeAsync(GeneratedMethod method, EventGraph graph, int index)
    {
        if (graph.StreamIdentity == StreamIdentity.AsGuid)
        {
            method.AssignMemberFromReaderAsync<IEvent>(null, index, x => x.StreamId);
        }
        else
        {
            method.AssignMemberFromReaderAsync<IEvent>(null, index, x => x.StreamKey);
        }
    }

    public void GenerateAppendCode(GeneratedMethod method, EventGraph graph, int index, AppendMode full)
    {
        if (graph.StreamIdentity == StreamIdentity.AsGuid)
        {
            method.SetParameterFromMember<StreamAction>(index, x => x.Id);
        }
        else
        {
            method.SetParameterFromMember<StreamAction>(index, x => x.Key);
        }
    }

    public string ValueSql(EventGraph graph, AppendMode mode)
    {
        return "?";
    }

    public void ReadValueSync(DbDataReader reader, int index, IEvent @event)
    {
        if (reader.IsDBNull(index)) return;
        _readSync.Value(reader, index, @event);
    }

    public async Task ReadValueAsync(DbDataReader reader, int index, IEvent @event, CancellationToken cancellation)
    {
        if (await reader.IsDBNullAsync(index, cancellation).ConfigureAwait(false)) return;
        await _readAsync.Value(reader, index, @event, cancellation).ConfigureAwait(false);
    }
}
