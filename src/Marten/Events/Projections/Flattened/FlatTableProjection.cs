#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ImTools;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Descriptors;
using JasperFx.Events.Projections;
using JasperFx.Events.Subscriptions;
using Marten.Events.Daemon;
using Marten.Linq.Parsing;
using Microsoft.Extensions.Logging;
using NpgsqlTypes;
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

    SubscriptionType ISubscriptionSource.Type => SubscriptionType.FlatTableProjection;
    ShardName[] ISubscriptionSource.ShardNames() => [new ShardName(Name, ShardName.All, Version)];

    Type ISubscriptionSource.ImplementationType => GetType();

    SubscriptionDescriptor ISubscriptionSource.Describe(IEventStore store)
    {
        return new SubscriptionDescriptor(this, store);
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

    IReadOnlyList<AsyncShard<IDocumentOperations, IQuerySession>> ISubscriptionSource<IDocumentOperations, IQuerySession>.Shards()
    {
        return
        [
            new AsyncShard<IDocumentOperations, IQuerySession>(Options, ShardRole.Projection,
                new ShardName(Name, ShardName.All, Version), this, this)
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

    ISubscriptionExecution ISubscriptionFactory<IDocumentOperations, IQuerySession>.BuildExecution(IEventStore<IDocumentOperations, IQuerySession> store, IEventDatabase database, ILoggerFactory loggerFactory,
        ShardName shardName)
    {
        var logger = loggerFactory.CreateLogger(GetType());
        return new ProjectionExecution<IDocumentOperations, IQuerySession>(shardName, Options, store, database, this, logger);
    }

    ISubscriptionExecution ISubscriptionFactory<IDocumentOperations, IQuerySession>.BuildExecution(IEventStore<IDocumentOperations, IQuerySession> store, IEventDatabase database, ILogger logger,
        ShardName shardName)
    {
        return new ProjectionExecution<IDocumentOperations, IQuerySession>(shardName, Options, store, database, this, logger);
    }

    bool IProjectionSource<IDocumentOperations, IQuerySession>.TryBuildReplayExecutor(IEventStore<IDocumentOperations, IQuerySession> store, IEventDatabase database,
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

    internal static IParameterSetter<IEvent> BuildPrimaryKeySetter<T>(MemberInfo[] members, StoreOptions storeOptions)
    {
        // It's off of IEvent directly
        if (members.Length == 1)
        {
            if (members[0].DeclaringType == typeof(IEvent))
            {
                return (IParameterSetter<IEvent>)BuildLeafParameterSetter(members[0], typeof(IEvent), storeOptions);
            }

            var setter = BuildLeafParameterSetter(members[0], typeof(T), storeOptions);
            return new EventForwarder<T>((IParameterSetter<T>)setter);
        }

        if (members.Length == 2)
        {
            var inner = BuildLeafParameterSetter(members[1], typeof(T), storeOptions);
            return new EventForwarder<T>((IParameterSetter<T>)inner);
        }

        return BuildSetterForMembers<T>(members.Skip(0).ToArray(), storeOptions);
    }

    internal static IParameterSetter<IEvent> BuildSetterForMembers<T>(MemberInfo[] members, StoreOptions storeOptions)
    {
        if (members.Length == 1)
        {
            if (members[0].DeclaringType != typeof(IEvent))
            {
                var inner = BuildLeafParameterSetter(members[0], typeof(T), storeOptions);
                return new EventForwarder<T>((IParameterSetter<T>)inner);
            }
        }

        // off of something on the event - MemberFinder.Determine returns the chain
        // outer-to-leaf, e.g. [Foo, Bar, Value]. Reversing puts the leaf first so
        // the ParameterSetter at the bottom does the actual parameter write, and
        // each RelayParameterSetter wraps it with the next level out (handling
        // intermediate nulls).
        var leafFirst = members.Reverse().ToArray();

        // Leaf decides the storage db type and any value transform (enum->string,
        // value-type unwrap, etc.). Use that db type all the way up so intermediate
        // nulls write DBNull with the correct type.
        var (dbType, _) = ResolveLeafStorage(leafFirst[0].GetRawMemberType()!, storeOptions);

        var setter = BuildLeafParameterSetter(leafFirst[0], leafFirst[1].GetRawMemberType()!, storeOptions);

        for (var i = 1; i < leafFirst.Length; i++)
        {
            setter = typeof(RelayParameterSetter<,>).CloseAndBuildAs<object>(dbType, setter,
                leafFirst[i], leafFirst[i].DeclaringType!, leafFirst[i].GetRawMemberType()!);
        }

        return new EventForwarder<T>((IParameterSetter<T>)setter);
    }

    /// <summary>
    /// Build a leaf <see cref="ParameterSetter{TSource,TValue}"/> for a single
    /// member off of a known source type. Honors <see cref="StoreOptions"/> for
    /// enum storage, registered value types, and nullable variants of either —
    /// scenarios <see cref="PostgresqlProvider.ToParameterType(Type)"/> can't
    /// resolve on its own.
    /// </summary>
    private static object BuildLeafParameterSetter(MemberInfo member, Type sourceType, StoreOptions storeOptions)
    {
        var memberType = member.GetRawMemberType()!;
        var (dbType, transform) = ResolveLeafStorage(memberType, storeOptions);

        // Default path (no special handling): preserve historical 1-arg construction
        // so we don't churn the existing tests / reflection layout.
        if (transform == null)
        {
            return typeof(ParameterSetter<,>).CloseAndBuildAs<object>(member, sourceType, memberType);
        }

        // Special path (enum / value type / nullable variants): use the explicit
        // (member, dbType, transform) constructor so PostgresqlProvider never has
        // to infer a parameter type from the .NET wrapper type.
        return typeof(ParameterSetter<,>).CloseAndBuildAs<object>(
            member, dbType, transform,
            sourceType, memberType);
    }

    /// <summary>
    /// Decide the PostgreSQL parameter type and any value-projection function
    /// needed for a member of the supplied (possibly nullable) .NET type.
    /// </summary>
    /// <returns>
    /// <c>(dbType, transform)</c> where <c>transform</c> is non-null when the
    /// .NET value needs projection (enum-to-string, value-type-to-inner) before
    /// being handed to Npgsql.
    /// </returns>
    internal static (NpgsqlDbType dbType, Func<object, object?>? transform) ResolveLeafStorage(Type memberType, StoreOptions storeOptions)
    {
        // Unwrap Nullable<T> for the type-introspection pass. The runtime setter
        // sees the nullable type as TValue and writes DBNull whenever the value is null;
        // only the non-null value hits the transform.
        var nonNullable = Nullable.GetUnderlyingType(memberType) ?? memberType;

        if (nonNullable.IsEnum)
        {
            if (storeOptions.Advanced.DuplicatedFieldEnumStorage == EnumStorage.AsString)
            {
                return (NpgsqlDbType.Varchar, raw => raw.ToString());
            }

            // AsInteger: convert each enum value to its underlying int. Use
            // Convert.ToInt32 rather than a direct cast so non-Int32-backed enums
            // (byte, short, long) round-trip into the int column without a runtime cast error.
            return (NpgsqlDbType.Integer, raw => Convert.ToInt32(raw));
        }

        var valueTypeInfo = storeOptions.TryFindValueType(nonNullable);
        if (valueTypeInfo != null)
        {
            var innerDbType = PostgresqlProvider.Instance.ToParameterType(valueTypeInfo.SimpleType);
            var valueProperty = valueTypeInfo.ValueProperty;
            return (innerDbType, raw => valueProperty.GetValue(raw));
        }

        return (PostgresqlProvider.Instance.ToParameterType(memberType), null);
    }
}
