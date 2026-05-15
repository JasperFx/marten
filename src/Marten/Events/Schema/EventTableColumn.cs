using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using Marten.Internal.CodeGeneration;
using Marten.Linq.Parsing;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema;

internal class EventTableColumn: TableColumn, IEventTableColumn
{
    private readonly Expression<Func<IEvent, object>> _eventMemberExpression;
    private readonly Lazy<Action<System.Data.Common.DbDataReader, int, IEvent>> _readSync;
    private readonly Lazy<Func<System.Data.Common.DbDataReader, int, IEvent, System.Threading.CancellationToken, System.Threading.Tasks.Task>> _readAsync;

    public EventTableColumn(string name, Expression<Func<IEvent, object>> eventMemberExpression): base(name, "varchar")
    {
        _eventMemberExpression = eventMemberExpression;
        Member = MemberFinder.Determine(eventMemberExpression).Single();
        var memberType = Member.GetMemberType();
        Type = PostgresqlProvider.Instance.GetDatabaseType(memberType, EnumStorage.AsInteger);
        NpgsqlDbType = PostgresqlProvider.Instance.ToParameterType(memberType);

        // Compiled-delegate readers for the closed-shape event-storage path (#4411).
        // Lazy so columns that are never read from (e.g., select-only columns
        // not exercised by the configured EventGraph) don't pay the
        // expression-compilation cost at construction.
        _readSync = new Lazy<Action<System.Data.Common.DbDataReader, int, IEvent>>(
            () => EventColumnReaders.BuildSync(_eventMemberExpression));
        _readAsync = new Lazy<Func<System.Data.Common.DbDataReader, int, IEvent, System.Threading.CancellationToken, System.Threading.Tasks.Task>>(
            () => EventColumnReaders.BuildAsync(_eventMemberExpression));
    }

    public MemberInfo Member { get; }

    public NpgsqlDbType NpgsqlDbType { get; set; }

    public void GenerateSelectorCodeSync(GeneratedMethod method, EventGraph graph, int index)
    {
        method.IfDbReaderValueIsNotNull(index, () =>
        {
            method.AssignMemberFromReader(null, index, _eventMemberExpression);
        });
    }

    public void GenerateSelectorCodeAsync(GeneratedMethod method, EventGraph graph, int index)
    {
        method.IfDbReaderValueIsNotNullAsync(index, () =>
        {
            method.AssignMemberFromReaderAsync(null, index, _eventMemberExpression);
        });
    }

    public virtual void GenerateAppendCode(GeneratedMethod method, EventGraph graph, int index, AppendMode full)
    {
        method.Frames.Code(
            $"var parameter{index} = parameterBuilder.{nameof(IGroupedParameterBuilder.AppendParameter)}({{0}}.{Member.Name});", Use.Type<IEvent>());

        method.Frames.Code($"parameter{index}.{nameof(NpgsqlParameter.NpgsqlDbType)} = {{0}};", NpgsqlDbType);

    }

    public virtual string ValueSql(EventGraph graph, AppendMode mode)
    {
        return "?";
    }

    public virtual void ReadValueSync(System.Data.Common.DbDataReader reader, int index, IEvent @event)
    {
        if (reader.IsDBNull(index)) return;
        _readSync.Value(reader, index, @event);
    }

    public virtual async System.Threading.Tasks.Task ReadValueAsync(
        System.Data.Common.DbDataReader reader, int index, IEvent @event, System.Threading.CancellationToken cancellation)
    {
        if (await reader.IsDBNullAsync(index, cancellation).ConfigureAwait(false)) return;
        await _readAsync.Value(reader, index, @event, cancellation).ConfigureAwait(false);
    }
}
