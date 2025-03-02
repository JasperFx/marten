#nullable enable

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core.Descriptions;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events.Daemon;
using Marten.Linq.Parsing;
using Weasel.Core;
using Weasel.Postgresql;
using IReplayExecutor = JasperFx.Events.Daemon.IReplayExecutor;
using Table = Weasel.Postgresql.Tables.Table;

namespace Marten.Events.Projections.Flattened;

/// <summary>
///     Projection type that will write event data to a single database table
/// </summary>
public partial class FlatTableProjection: ProjectionBase, IProjectionSource<IDocumentOperations, IQuerySession>,
    IProjectionSchemaSource
{
    private readonly List<IEventHandler> _handlers = new();
    private readonly string _inlineTypeName;

    public FlatTableProjection(string tableName, SchemaNameSource schemaNameSource): this(
        new PostgresqlObjectName("public", tableName), schemaNameSource)
    {
    }

    public FlatTableProjection(DbObjectName tableName): this(tableName, SchemaNameSource.Explicit) { }

    private FlatTableProjection(DbObjectName tableName, SchemaNameSource schemaNameSource)
    {
        SchemaNameSource = schemaNameSource;
        Table = new Table(tableName);
        _inlineTypeName = GetType().ToSuffixedTypeName("InlineProjection");
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

        foreach (var handler in _handlers)
        foreach (var schemaObject in handler.BuildObjects(events, Table))
            yield return schemaObject;
    }

    public Type ProjectionType => GetType();
    public string Name => ProjectionName;
    public uint Version => ProjectionVersion;

    public SubscriptionDescriptor Describe()
    {
        return new SubscriptionDescriptor(this, SubscriptionType.FlatTableProjection);
    }

    public IReadOnlyList<AsyncShard<IDocumentOperations, IQuerySession>> Shards()
    {
        throw new NotImplementedException();
    }

    public bool TryBuildReplayExecutor(IEventStorage<IDocumentOperations, IQuerySession> store, IEventDatabase database,
        out IReplayExecutor executor)
    {
        throw new NotImplementedException();
    }

    public IInlineProjection<IDocumentOperations> BuildForInline()
    {
        throw new NotImplementedException();
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

        _handlers.Add(map);
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

        _handlers.Add(new EventDeleter(typeof(T), members));
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
}
