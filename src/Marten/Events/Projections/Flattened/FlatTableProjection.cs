#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Descriptions;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using JasperFx.Events.Subscriptions;
using Marten.Events.Daemon;
using Marten.Linq.Parsing;
using Microsoft.Extensions.Logging;
using Weasel.Core;
using Weasel.Postgresql;
using IReplayExecutor = JasperFx.Events.Daemon.IReplayExecutor;
using Table = Weasel.Postgresql.Tables.Table;

namespace Marten.Events.Projections.Flattened;

/// <summary>
///     Projection type that will write event data to a single database table
/// </summary>
public partial class FlatTableProjection: ProjectionBase, IProjectionSource<IDocumentOperations, IQuerySession>,
    IProjectionSchemaSource, IInlineProjection<IDocumentOperations>, IJasperFxProjection<IDocumentOperations>
{
    private ImHashMap<Type, IEventHandler> _handlers = ImHashMap<Type, IEventHandler>.Empty;

    public FlatTableProjection(string tableName, SchemaNameSource schemaNameSource): this(
        new PostgresqlObjectName("public", tableName), schemaNameSource)
    {
    }

    public FlatTableProjection(DbObjectName tableName): this(tableName, SchemaNameSource.Explicit) { }

    private FlatTableProjection(DbObjectName tableName, SchemaNameSource schemaNameSource)
    {
        SchemaNameSource = schemaNameSource;
        Table = new Table(tableName);
    }

    public SchemaNameSource SchemaNameSource { get; }

    /// <summary>
    ///     The definition of the table being written to. You can use this to
    ///     modify the table structure or even add indexes
    /// </summary>
    public Table Table { get; }

    IEnumerable<ISchemaObject> IProjectionSchemaSource.CreateSchemaObjects(EventGraph events)
    {
        readSchema(events);

        yield return Table;

        foreach (var handler in _handlers.Enumerate().Select(x => x.Value))
        {
            foreach (var schemaObject in handler.BuildObjects(events, Table))
            {
                yield return schemaObject;
            }
        }

        foreach (var entry in _handlers.Enumerate())
        {
            entry.Value.Compile(events, Table);
        }

    }

    public Type ProjectionType => GetType();
    public string Name => ProjectionName;
    public uint Version => ProjectionVersion;

    public SubscriptionDescriptor Describe()
    {
        return new SubscriptionDescriptor(this, SubscriptionType.FlatTableProjection);
    }

    IReadOnlyList<AsyncShard<IDocumentOperations, IQuerySession>> ISubscriptionSource<IDocumentOperations, IQuerySession>.Shards()
    {
        return
        [
            new AsyncShard<IDocumentOperations, IQuerySession>(Options, ShardRole.Projection,
                new ShardName(ProjectionName, ShardName.All), this, this)
        ];
    }

    private void apply(IDocumentOperations operations, IReadOnlyList<IEvent> events)
    {
        foreach (var @event in events)
        {
            if (_handlers.TryFind(@event.EventType, out var handler))
            {
                handler.Handle(operations, @event);
            }
        }
    }

    Task IJasperFxProjection<IDocumentOperations>.ApplyAsync(IDocumentOperations operations, IReadOnlyList<IEvent> events, CancellationToken cancellation)
    {
        apply(operations, events);
        return Task.CompletedTask;
    }

    ISubscriptionExecution ISubscriptionFactory<IDocumentOperations, IQuerySession>.BuildExecution(IEventStorage<IDocumentOperations, IQuerySession> storage, IEventDatabase database, ILoggerFactory loggerFactory,
        ShardName shardName)
    {
        var logger = loggerFactory.CreateLogger(GetType());
        return new ProjectionExecution<IDocumentOperations, IQuerySession>(shardName, Options, storage, database, this, logger);
    }

    ISubscriptionExecution ISubscriptionFactory<IDocumentOperations, IQuerySession>.BuildExecution(IEventStorage<IDocumentOperations, IQuerySession> storage, IEventDatabase database, ILogger logger,
        ShardName shardName)
    {
        return new ProjectionExecution<IDocumentOperations, IQuerySession>(shardName, Options, storage, database, this, logger);
    }

    bool IProjectionSource<IDocumentOperations, IQuerySession>.TryBuildReplayExecutor(IEventStorage<IDocumentOperations, IQuerySession> store, IEventDatabase database,
        out IReplayExecutor executor)
    {
        executor = default;
        return false;
    }

    IInlineProjection<IDocumentOperations> IProjectionSource<IDocumentOperations, IQuerySession>.BuildForInline()
    {
        return this;
    }

    public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams, CancellationToken cancellation)
    {
        apply(operations, streams.SelectMany(x => x.Events).ToList());
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Write values for the designated event type to the table columns
    /// </summary>
    /// <param name="configure"></param>
    /// <param name="tablePrimaryKeySource">
    ///     If specified, tells the projection how to find the primary key value for the table
    ///     from the event data. If missing, the projection uses the event's stream identity by default
    /// </param>
    /// <typeparam name="T"></typeparam>
    public void Project<T>(Action<StatementMap<T>> configure, Expression<Func<T, object>>? tablePrimaryKeySource = null)
    {
        IncludeType<T>();

        assertReceivedEventType<T>();

        var members = tablePrimaryKeySource == null
            ? Array.Empty<MemberInfo>()
            : MemberFinder.Determine(tablePrimaryKeySource);

        var map = new StatementMap<T>(this, members);

        configure(map);

        _handlers = _handlers.AddOrUpdate(typeof(T), map);
    }

    private static void assertReceivedEventType<T>()
    {
        if (typeof(T).Closes(typeof(IEvent<>)))
        {
            throw new ArgumentOutOfRangeException("T",
                "IEvent<T> cannot be used as the event type in this usage. Please use the actual event type");
        }

        if (!typeof(T).IsConcrete())
        {
            throw new ArgumentOutOfRangeException("T",
                "The event type in this usage must be a specific concrete event type");
        }
    }

    /// <summary>
    ///     Direct the projection to delete a row when this event type is encountered
    /// </summary>
    /// <param name="tablePrimaryKeySource">
    ///     If specified, tells the projection how to find the primary key value for the table
    ///     from the event data. If missing, the projection uses the event's stream identity by default
    /// </param>
    /// <typeparam name="T"></typeparam>
    public void Delete<T>(Expression<Func<T, object>>? tablePrimaryKeySource = null)
    {
        IncludeType<T>();
        assertReceivedEventType<T>();

        var members = tablePrimaryKeySource == null
            ? Array.Empty<MemberInfo>()
            : MemberFinder.Determine(tablePrimaryKeySource);

        _handlers = _handlers.AddOrUpdate(typeof(T), new EventDeleter<T>(members, Table));
    }

    private void readSchema(EventGraph events)
    {
        switch (SchemaNameSource)
        {
            case SchemaNameSource.DocumentSchema:
                Table.MoveToSchema(events.Options.DatabaseSchemaName);
                break;

            case SchemaNameSource.EventSchema:
                Table.MoveToSchema(events.DatabaseSchemaName);
                break;
        }

        Options.DeleteDataInTableOnTeardown(Table.Identifier);
    }

    internal static IParameterSetter<IEvent> BuildPrimaryKeySetter<T>(MemberInfo[] members)
    {
        // It's off of IEvent directly
        if (members.Length == 1)
        {
            if (members[0].DeclaringType == typeof(IEvent))
            {
                return typeof(ParameterSetter<,>).CloseAndBuildAs<IParameterSetter<IEvent>>(members[0], typeof(IEvent), members[0].GetRawMemberType());
            }

            var setter = typeof(ParameterSetter<,>).CloseAndBuildAs<IParameterSetter<T>>(members[0], typeof(T), members[0].GetRawMemberType());
            return new EventForwarder<T>(setter);
        }

        if (members.Length == 2)
        {
            var inner = typeof(ParameterSetter<,>).CloseAndBuildAs<IParameterSetter<T>>(members[1], typeof(T),
                members[1].GetRawMemberType());

            return new EventForwarder<T>(inner);
        }

        return BuildSetterForMembers<T>(members.Skip(0).ToArray());
    }

    internal static IParameterSetter<IEvent> BuildSetterForMembers<T>(MemberInfo[] members)
    {
        if (members.Length == 1)
        {
            if (members[0].DeclaringType != typeof(IEvent))
            {
                var inner = typeof(ParameterSetter<,>).CloseAndBuildAs<IParameterSetter<T>>(members[0],typeof(T), members[0].GetRawMemberType());
                return new EventForwarder<T>(inner);
            }
        }

        // off of something on the event
        var outsideToInside = members.Reverse().ToArray();

        var dbType = PostgresqlProvider.Instance.ToParameterType(outsideToInside[0].GetRawMemberType());
        var setter = typeof(ParameterSetter<,>).CloseAndBuildAs<object>(outsideToInside[0], outsideToInside[1].GetRawMemberType(),
            outsideToInside[0].GetRawMemberType());

        for (int i = 1; i < outsideToInside.Length; i++)
        {
            setter = typeof(RelayParameterSetter<,>).CloseAndBuildAs<object>(dbType, setter, outsideToInside[i], outsideToInside[i].DeclaringType, outsideToInside[i].GetRawMemberType());
        }

        return new EventForwarder<T>((IParameterSetter<T>)setter);
    }
}
