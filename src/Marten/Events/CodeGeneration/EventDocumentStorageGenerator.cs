using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.RuntimeCompiler;
using Marten.Events.Archiving;
using Marten.Events.Operations;
using Marten.Events.Querying;
using Marten.Events.Schema;
using Marten.Internal;
using Marten.Internal.CodeGeneration;
using Marten.Schema;
using Marten.Storage;
using Marten.Storage.Metadata;
using Npgsql;
using Weasel.Postgresql;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Events.CodeGeneration;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
internal static class EventDocumentStorageGenerator
{
    private const string StreamStateSelectorTypeName = "GeneratedStreamStateQueryHandler";
    private const string InsertStreamOperationName = "GeneratedInsertStream";
    private const string UpdateStreamVersionOperationName = "GeneratedStreamVersionOperation";
    internal const string EventDocumentStorageTypeName = "GeneratedEventDocumentStorage";

    /// <summary>
    ///     Only for testing support
    /// </summary>
    /// <param name="options"></param>
    /// <returns></returns>
    public static EventDocumentStorage GenerateStorage(StoreOptions options)
    {
        var assembly =
            new GeneratedAssembly(new GenerationRules(SchemaConstants.MartenGeneratedNamespace + ".EventStore"));
        var builderType = AssembleTypes(options, assembly);


        var compiler = new AssemblyGenerator();
        compiler.ReferenceAssembly(typeof(IMartenSession).Assembly);
        compiler.Compile(assembly);

        Debug.WriteLine(builderType.SourceCode);

        return (EventDocumentStorage)Activator.CreateInstance(builderType.CompiledType!, options)!;
    }

    public static GeneratedType AssembleTypes(StoreOptions options, GeneratedAssembly assembly)
    {
        assembly.ReferenceAssembly(typeof(EventGraph).Assembly);

        var builderType = assembly.AddType(EventDocumentStorageTypeName, typeof(EventDocumentStorage));

        buildSelectorMethods(options, builderType);

        buildAppendEventOperations(options, assembly, builderType);


        buildInsertStream(builderType, assembly, options.EventGraph);

        buildStreamQueryHandlerType(options.EventGraph, assembly);

        buildQueryForStreamMethod(options.EventGraph, builderType);

        buildUpdateStreamVersion(builderType, assembly, options.EventGraph);

        return builderType;
    }

    private static void buildAppendEventOperations(StoreOptions options, GeneratedAssembly assembly,
        GeneratedType builderType)
    {
        var appendEventOperationType = buildAppendEventOperation(options.EventGraph, assembly, AppendMode.Full);
        builderType.MethodFor(nameof(EventDocumentStorage.AppendEvent))
            .Frames.ReturnNewGeneratedTypeObject(appendEventOperationType, "stream", "e");

        var quickAppendEventGivenVersion =
            buildAppendEventOperation(options.EventGraph, assembly, AppendMode.QuickWithVersion);
        builderType.MethodFor(nameof(EventDocumentStorage.QuickAppendEventWithVersion))
            .Frames.ReturnNewGeneratedTypeObject(quickAppendEventGivenVersion, "stream", "e");

        var quickAppend = buildQuickAppendOperation(options.EventGraph, assembly);
        builderType.MethodFor(nameof(EventDocumentStorage.QuickAppendEvents))
            .Frames.ReturnNewGeneratedTypeObject(quickAppend, "stream");
    }

    private static void buildSelectorMethods(StoreOptions options, GeneratedType builderType)
    {
        var sync = builderType.MethodFor(nameof(EventDocumentStorage.ApplyReaderDataToEvent));
        var async = builderType.MethodFor(nameof(EventDocumentStorage.ApplyReaderDataToEventAsync));

        // The json data column has to go first
        var table = new EventsTable(options.EventGraph);
        var columns = table.SelectColumns();

        for (var i = 3; i < columns.Count; i++)
        {
            columns[i].GenerateSelectorCodeSync(sync, options.EventGraph, i);
            columns[i].GenerateSelectorCodeAsync(async, options.EventGraph, i);
        }
    }

    private static GeneratedType buildUpdateStreamVersion(GeneratedType builderType, GeneratedAssembly assembly,
        EventGraph graph)
    {
        var operationType = assembly.AddType(UpdateStreamVersionOperationName, typeof(UpdateStreamVersion));

        var sql = $"update {graph.DatabaseSchemaName}.mt_streams ";

        var configureCommand = operationType.MethodFor("ConfigureCommand");
        configureCommand.DerivedVariables.Add(
            new Variable(typeof(StreamAction), nameof(UpdateStreamVersion.Stream)));

        configureCommand.Frames.AppendSql(sql);

        configureCommand.Frames.Code($"var parameterBuilder = {{0}}.{nameof(CommandBuilder.CreateGroupedParameterBuilder)}();", Use.Type<ICommandBuilder>());

        configureCommand.Frames.AppendSql("set version = ");
        configureCommand.SetParameterFromMember<StreamAction>(0, x => x.Version);

        configureCommand.Frames.AppendSql(" where id = ");
        if (graph.StreamIdentity == StreamIdentity.AsGuid)
        {
            configureCommand.SetParameterFromMember<StreamAction>(1, x => x.Id);
        }
        else
        {
            configureCommand.SetParameterFromMember<StreamAction>(1, x => x.Key);
        }

        configureCommand.Frames.AppendSql(" and version = ");
        configureCommand.SetParameterFromMember<StreamAction>(2, x => x.ExpectedVersionOnServer);

        if (graph.TenancyStyle == TenancyStyle.Conjoined)
        {
            configureCommand.Frames.AppendSql($" and {TenantIdColumn.Name} = ");
            new TenantIdColumn().As<IStreamTableColumn>().GenerateAppendCode(configureCommand, 3);
        }

        configureCommand.Frames.AppendSql(" returning version");

        builderType.MethodFor(nameof(EventDocumentStorage.UpdateStreamVersion))
            .Frames.Code($"return new {assembly.Namespace}.{UpdateStreamVersionOperationName}({{0}});",
                Use.Type<StreamAction>());

        return operationType;
    }

    private static void buildQueryForStreamMethod(EventGraph graph, GeneratedType builderType)
    {
        var arguments = new List<string>
        {
            graph.StreamIdentity == StreamIdentity.AsGuid
                ? $"stream.{nameof(StreamAction.Id)}"
                : $"stream.{nameof(StreamAction.Key)}"
        };

        if (graph.TenancyStyle == TenancyStyle.Conjoined)
        {
            arguments.Add($"stream.{nameof(StreamAction.TenantId)}");
        }

        builderType.MethodFor(nameof(EventDocumentStorage.QueryForStream))
            .Frames.Code(
                $"return new {builderType.ParentAssembly.Namespace}.{StreamStateSelectorTypeName}({arguments.Join(", ")});");
    }

    private static GeneratedType buildStreamQueryHandlerType(EventGraph graph, GeneratedAssembly assembly)
    {
        var streamQueryHandlerType =
            assembly.AddType(StreamStateSelectorTypeName, typeof(StreamStateQueryHandler));

        streamQueryHandlerType.AllInjectedFields.Add(graph.StreamIdentity == StreamIdentity.AsGuid
            ? new InjectedField(typeof(Guid), "streamId")
            : new InjectedField(typeof(string), "streamId"));

        buildConfigureCommandMethodForStreamState(graph, streamQueryHandlerType);

        // Row reading is no longer codegen'd here. The base StreamStateQueryHandler
        // delegates Handle / HandleAsync to ISelector<StreamState> on IEventStorage,
        // so the SELECT clause and the row read share a single definition site
        // (EventDocumentStorage.StreamStateSelectSql + the explicit interface
        // implementation alongside it). See JasperFx/marten#4347.
        return streamQueryHandlerType;
    }

    private static void buildConfigureCommandMethodForStreamState(EventGraph graph,
        GeneratedType streamQueryHandlerType)
    {
        if (graph.TenancyStyle == TenancyStyle.Conjoined)
        {
            streamQueryHandlerType.AllInjectedFields.Add(new InjectedField(typeof(string), "tenantId"));
        }

        var configureCommand = streamQueryHandlerType.MethodFor("ConfigureCommand");

        // Pull the SELECT clause from the storage so the codegen and any other
        // consumer (e.g. the IEventStore explorer surface) share one definition of
        // "the column list that matches ISelector<StreamState>.Resolve".
        var selectSql = BuildStreamStateSelectSql(graph);
        configureCommand.Frames.AppendSql(selectSql + " where id = ");

        var idDbType = graph.StreamIdentity == StreamIdentity.AsGuid ? DbType.Guid : DbType.String;
        configureCommand.Frames.Code($"var parameter1 = builder.{nameof(CommandBuilder.AppendParameter)}(_streamId);");
        configureCommand.Frames.Code("parameter1.DbType = {0};", idDbType);

        if (graph.TenancyStyle == TenancyStyle.Conjoined)
        {
            configureCommand.Frames.AppendSql($" and {TenantIdColumn.Name} = ");
            configureCommand.Frames.Code($"var parameter2 = builder.{nameof(CommandBuilder.AppendParameter)}(_tenantId);");
            configureCommand.Frames.Code("parameter2.DbType = {0};", DbType.String);
        }
    }

    /// <summary>
    ///     Single source of truth for the StreamState SELECT clause. Kept as a static
    ///     helper alongside the codegen because the column ordering is coupled to
    ///     <see cref="EventDocumentStorage"/>'s <see cref="ISelector{StreamState}"/>
    ///     implementation — they must be edited together.
    /// </summary>
    internal static string BuildStreamStateSelectSql(EventGraph graph) =>
        $"select id, version, type, timestamp, created, is_archived from {graph.DatabaseSchemaName}.mt_streams";

    private static GeneratedType buildAppendEventOperation(EventGraph graph, GeneratedAssembly assembly,
        AppendMode mode)
    {
        var typeName = "AppendEventOperation";
        if (mode != AppendMode.Full)
        {
            typeName += mode.ToString();
        }

        var baseType = typeof(AppendEventOperationBase);
        var operationType = assembly.AddType(typeName, baseType);

        var configure = operationType.MethodFor(nameof(AppendEventOperationBase.ConfigureCommand));
        configure.DerivedVariables.Add(new Variable(typeof(IEvent), nameof(AppendEventOperationBase.Event)));
        configure.DerivedVariables.Add(new Variable(typeof(StreamAction), nameof(AppendEventOperationBase.Stream)));

        var columns = new EventsTable(graph).SelectColumns()

            // Hokey, use an explicit model for writeable vs readable columns some day
            .Where(x => x is not IsArchivedColumn).ToList();

        // Hokey, but we need to move Sequence to the end
        var sequence = columns.OfType<SequenceColumn>().Single();
        columns.Remove(sequence);
        columns.Add(sequence);

        var sql =
            $"insert into {graph.DatabaseSchemaName}.mt_events ({columns.Select(x => x.Name).Join(", ")}) values (";

        configure.Frames.AppendSql(sql);

        configure.Frames.Code($"var parameterBuilder = {{0}}.{nameof(CommandBuilder.CreateGroupedParameterBuilder)}(',');", Use.Type<ICommandBuilder>());

        for (var i = 0; i < columns.Count; i++)
        {
            columns[i].GenerateAppendCode(configure, graph, i, mode);
            var valueSql = columns[i].ValueSql(graph, mode);
            if (valueSql != "?")
                configure.Frames.AppendSql($"{(i > 0 ? "," : string.Empty)}{valueSql}");
        }

        configure.Frames.AppendSql(')');

        return operationType;
    }

    private static GeneratedType buildQuickAppendOperation(EventGraph graph, GeneratedAssembly assembly)
    {
        var operationType = assembly.AddType("QuickAppendEventsOperation", typeof(QuickAppendEventsOperationBase));

        var table = new EventsTable(graph);

        var sql = $"select {graph.DatabaseSchemaName}.mt_quick_append_events(";

        var configure = operationType.MethodFor(nameof(QuickAppendEventsOperationBase.ConfigureCommand));
        configure.DerivedVariables.Add(new Variable(typeof(StreamAction), nameof(QuickAppendEventsOperationBase.Stream)));

        configure.Frames.AppendSql(sql);

        configure.Frames.Code($"var parameterBuilder = {{0}}.{nameof(CommandBuilder.CreateGroupedParameterBuilder)}(',');", Use.Type<ICommandBuilder>());

        if (graph.StreamIdentity == StreamIdentity.AsGuid)
        {
            configure.Frames.Code("writeId(parameterBuilder);");
        }
        else
        {
            configure.Frames.Code("writeKey(parameterBuilder);");
        }

        configure.Frames.Code("writeBasicParameters(parameterBuilder, session);");

        if (table.Columns.OfType<CausationIdColumn>().Any())
        {
            configure.Frames.Code("writeCausationIds(parameterBuilder);");
        }

        if (table.Columns.OfType<CorrelationIdColumn>().Any())
        {
            configure.Frames.Code("writeCorrelationIds(parameterBuilder);");
        }

        if (table.Columns.OfType<HeadersColumn>().Any())
        {
            configure.Frames.Code("writeHeaders(parameterBuilder, session);");
        }

        if (table.Columns.OfType<UserNameColumn>().Any())
        {
            configure.Frames.Code("writeUserNames(parameterBuilder, session);");
        }

        if (graph.AppendMode == EventAppendMode.QuickWithServerTimestamps)
        {
            configure.Frames.Code("writeTimestamps(parameterBuilder);");
        }

        // HStore mode writes tags via a follow-up UPDATE; the function signature
        // does not include the per-tag varchar[] parameters in that case.
        if (graph.TagTypes.Count > 0 && graph.DcbStorageMode != DcbStorageMode.HStore)
        {
            configure.Frames.Code("writeAllTagValues(parameterBuilder);");
        }

        configure.Frames.AppendSql(')');

        return operationType;
    }

    private static GeneratedType buildInsertStream(GeneratedType builderType, GeneratedAssembly generatedAssembly,
        EventGraph graph)
    {
        var operationType = generatedAssembly.AddType(InsertStreamOperationName, typeof(InsertStreamBase));

        var columns = new StreamsTable(graph)
            .Columns
            .OfType<IStreamTableColumn>()
            .Where(x => x.Writes)
            .ToArray();

        var configureCommand = operationType.MethodFor("ConfigureCommand");
        configureCommand.DerivedVariables.Add(new Variable(typeof(StreamAction), nameof(InsertStreamBase.Stream)));

        // Strict identity enforcement: wrap the mt_streams insert in a modifying
        // CTE so we can also INSERT into the non-partitioned mt_streams_identity
        // tracking table in the same prepared statement. Postgres rejects
        // semicolon-separated multi-statement prepared statements ("cannot insert
        // multiple commands into a prepared statement"), so a single statement
        // with a data-modifying CTE is the right shape here.
        // PostgreSQL surfaces unique violations from the chained INSERT with
        // TableName = "mt_streams_identity"; InsertStreamBase.matches() recognizes
        // that name and translates it into ExistingStreamIdCollisionException.
        if (graph.EnableStrictStreamIdentityEnforcement)
        {
            var identityColumnNames = graph.TenancyStyle == TenancyStyle.Conjoined
                ? "tenant_id, id"
                : "id";

            var cteHead =
                $"with new_stream as (insert into {graph.DatabaseSchemaName}.mt_streams ({columns.Select(x => x.Name).Join(", ")}) values (";
            configureCommand.Frames.AppendSql(cteHead);
        }
        else
        {
            var sql =
                $"insert into {graph.DatabaseSchemaName}.mt_streams ({columns.Select(x => x.Name).Join(", ")}) values (";
            configureCommand.Frames.AppendSql(sql);
        }

        configureCommand.Frames.Code($"var parameterBuilder = {{0}}.{nameof(CommandBuilder.CreateGroupedParameterBuilder)}(',');", Use.Type<ICommandBuilder>());

        for (var i = 0; i < columns.Length; i++)
        {
            columns[i].GenerateAppendCode(configureCommand, i);
        }

        if (graph.EnableStrictStreamIdentityEnforcement)
        {
            var identityColumnNames = graph.TenancyStyle == TenancyStyle.Conjoined
                ? "tenant_id, id"
                : "id";

            // Close the CTE's INSERT, return the identity columns, then chain the
            // identity-table INSERT off the CTE so it sees the same row that
            // mt_streams just wrote. No new parameters needed — we're piping
            // values through the CTE.
            configureCommand.Frames.AppendSql(
                $") returning {identityColumnNames}) " +
                $"insert into {graph.DatabaseSchemaName}.{StreamIdentityEnforcementTable.TableName} ({identityColumnNames}) " +
                $"select {identityColumnNames} from new_stream");
        }
        else
        {
            configureCommand.Frames.AppendSql(')');
        }

        builderType.MethodFor(nameof(EventDocumentStorage.InsertStream))
            .Frames.ReturnNewGeneratedTypeObject(operationType, "stream");

        return operationType;
    }
}
