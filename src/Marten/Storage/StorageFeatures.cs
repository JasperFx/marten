#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using ImTools;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Descriptors;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using Marten.Events;
using Marten.Events.Daemon;
using StreamState = JasperFx.Events.StreamState;
using Marten.Exceptions;
using Marten.Schema;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Storage;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("Trimming", "IL2070",
    Justification = "Class-level: reflects PublicMethods/PublicProperties on a Type whose runtime instance is preserved at the StoreOptions / projection-registration boundary.")]
[UnconditionalSuppressMessage("Trimming", "IL2087",
    Justification = "Class-level: generic method/type argument flows reflective Type values into a DAM-annotated target. Source preserved at the registration boundary.")]
[UnconditionalSuppressMessage("Trimming", "IL2090",
    Justification = "Class-level: generic class type-argument flow on the aggregator / storage instantiation. Types preserved at the projection-registration boundary.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
public class StorageFeatures: IFeatureSchema, IDescribeMyself
{
    private readonly Ref<ImHashMap<Type, IDocumentMappingBuilder>> _builders =
        Ref.Of(ImHashMap<Type, IDocumentMappingBuilder>.Empty);

    private readonly ThreadLocal<IList<Type>> _buildingList = new();

    private readonly Ref<ImHashMap<Type, DocumentMapping>> _documentMappings =
        Ref.Of(ImHashMap<Type, DocumentMapping>.Empty);

    private readonly Ref<ImHashMap<Type, IFeatureSchema>> _features =
        Ref.Of(ImHashMap<Type, IFeatureSchema>.Empty);

    private readonly Ref<ImHashMap<Type, IDocumentMapping>> _mappings =
        Ref.Of(ImHashMap<Type, IDocumentMapping>.Empty);

    private readonly StoreOptions _options;

    private ImHashMap<Type, IEnumerable<Type>> _typeDependencies = ImHashMap<Type, IEnumerable<Type>>.Empty;

    internal StorageFeatures(StoreOptions options)
    {
        _options = options;

        SystemFunctions = new SystemFunctions(options);
    }

    /// <summary>
    ///     Additional Postgresql tables, functions, or sequences to be managed by this DocumentStore
    /// </summary>
    public List<ISchemaObject> ExtendedSchemaObjects { get; } = new();

    internal SystemFunctions SystemFunctions { get; }

    internal IEnumerable<DocumentMapping> AllDocumentMappings =>
        _documentMappings.Value.Enumerate().Select(x => x.Value);

    internal IEnumerable<DocumentMapping> DocumentMappingsWithSchema =>
        _documentMappings.Value.Enumerate().Where(x => !x.Value.SkipSchemaGeneration).Select(x => x.Value);

    /// <summary>
    ///     Registered document types — i.e. anything that <see cref="MartenRegistry.For{T}"/>
    ///     queued for mapping, plus anything Marten itself registered (e.g. DeadLetterEvent).
    ///     Reads only the registration map; does <b>not</b> trigger <see cref="DocumentMapping.CompileAndValidate"/>
    ///     and therefore preserves the lazy-materialisation invariant from
    ///     <see href="https://github.com/JasperFx/marten/issues/4303">marten#4303</see>
    ///     (configuration errors must surface on first session use, not at <c>DocumentStore.For</c>).
    ///     Order is registration order, which is not stable across runs — callers that hash
    ///     this list must sort it.
    /// </summary>
    internal IEnumerable<Type> RegisteredDocumentTypes =>
        _builders.Value.Enumerate().Select(pair => pair.Key);

    void IFeatureSchema.WritePermissions(Migrator rules, TextWriter writer)
    {
        // Nothing
    }

    IEnumerable<Type> IFeatureSchema.DependentTypes()
    {
        yield break;
    }

    ISchemaObject[] IFeatureSchema.Objects => ExtendedSchemaObjects.ToArray();

    string IFeatureSchema.Identifier => "Extended";

    Migrator IFeatureSchema.Migrator => _options.Advanced.Migrator;

    Type IFeatureSchema.StorageType => typeof(StorageFeatures);

    internal DocumentMapping Build(Type type, StoreOptions options)
    {
        if (_buildingList.IsValueCreated)
        {
            if (_buildingList.Value!.Contains(type))
            {
                throw new InvalidOperationException(
                    $"Cyclic dependency between documents detected. The types are: {_buildingList.Value.Select(x => x.FullNameInCode()).Join(", ")}");
            }
        }
        else
        {
            _buildingList.Value = new List<Type>();
        }

        _buildingList.Value.Add(type);

        if (_builders.Value.TryFind(type, out var builder))
        {
            var mapping = builder.Build(options);
            _options.applyPostPolicies(mapping);
            _buildingList.Value.Remove(type);
            return mapping;
        }

        _buildingList.Value.Remove(type);
        var fallbackBuilder = typeof(DocumentMappingBuilder<>)
            .CloseAndBuildAs<IDocumentMappingBuilder>(type);
        var m = fallbackBuilder.Build(options);
        _options.applyPostPolicies(m);
        return m;
    }

    internal void RegisterDocumentType(Type documentType)
    {
        if (!_builders.Value.Contains(documentType))
        {
            _builders.Swap(d => d.AddOrUpdate(documentType,
                typeof(DocumentMappingBuilder<>).CloseAndBuildAs<IDocumentMappingBuilder>(documentType))
            );
        }
    }

    internal DocumentMappingBuilder<T> BuilderFor<T>()
    {
        if (_builders.Value.TryFind(typeof(T), out var builder))
        {
            return (DocumentMappingBuilder<T>)builder;
        }

        builder = new DocumentMappingBuilder<T>();
        _builders.Swap(d => d.AddOrUpdate(typeof(T), builder));

        return (DocumentMappingBuilder<T>)builder;
    }

    internal void BuildAllMappings()
    {
        foreach (var pair in _builders.Value.ToArray())
        {
            // 9.0: DeadLetterEvent is registered unconditionally in
            // ApplyConfiguration but is only meant to participate in schema
            // generation when the event store is active (#4303). The eager
            // pre-#4303 path skipped this implicitly because materialization
            // was driven by AllActiveFeatures' conditional MappingFor call;
            // now that we're forcing materialization explicitly, replicate
            // that gate here so we don't conjure a dlq table for stores that
            // never use the event store.
            if (pair.Key == typeof(DeadLetterEvent)
                && !_options.Events.As<EventGraph>().IsActive(_options))
            {
                continue;
            }

            // Just forcing them all to be built
            FindMapping(pair.Key);
        }

        // This needs to be done second so that it can pick up any subclass
        // relationships
        foreach (var documentType in _options.Projections.AllPublishedTypes()) FindMapping(documentType);
    }

    /// <summary>
    ///     Register custom storage features
    /// </summary>
    /// <param name="feature"></param>
    public void Add(IFeatureSchema feature)
    {
        if (!_features.Value.Contains(feature.StorageType))
        {
            _features.Swap(d => d.AddOrUpdate(feature.StorageType, feature));
        }
    }

    /// <summary>
    ///     Register custom storage features by type. Type must have either a no-arg, public
    ///     constructor or a constructor that takes in a single StoreOptions parameter
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public void Add<T>() where T : IFeatureSchema
    {
        var ctor = typeof(T).GetTypeInfo().GetConstructor(new[] { typeof(StoreOptions) });

        IFeatureSchema feature;
        if (ctor != null)
        {
            feature = Activator.CreateInstance(typeof(T), _options)!
                .As<IFeatureSchema>();
        }
        else
        {
            feature = Activator.CreateInstance(typeof(T))!.As<IFeatureSchema>();
        }

        Add(feature);
    }

    internal DocumentMapping MappingFor(Type documentType)
    {
        if (!_documentMappings.Value.TryFind(documentType, out var value))
        {
            // Whether this type was explicitly registered (via Schema.For<T>()
            // or AddMappingFor) decides whether we apply CompileAndValidate.
            // Master's eager pre-#4303 path only validated registered types;
            // fallback-built mappings for live-aggregation-only targets (no
            // Id, no persistence) deliberately skipped validation. Preserve
            // that distinction now that validation is lazy.
            var explicitlyRegistered = _builders.Value.TryFind(documentType, out _);

            value = Build(documentType, _options);
            _documentMappings.Swap(d => d.AddOrUpdate(documentType, value));

            // 9.0: Validation moved here from ApplyConfiguration's eager loop
            // (#4303). Each newly-materialized mapping is validated on first
            // access so AddMarten no longer has to walk every registered
            // document type, but configuration errors still surface — they
            // surface on the first session that touches the affected type
            // rather than at host build time. Documented in the 9.0 migration
            // guide as a behavioral shift. Live-aggregation-only types
            // (e.g. an aggregate without an Id used solely with
            // AggregateStreamAsync<T>) are not validated to match master.
            if (explicitlyRegistered)
            {
                value.CompileAndValidate();
            }

            // 9.0: Flatten this mapping's SubClasses into _mappings and
            // _features as part of materialization so subclass-only callers
            // (FindMapping, FindFeature, BulkInsertAsync<SubClass>, schema
            // migration for a sub-type) don't need a prior eager pass.
            // Mirrors what PostProcessConfiguration used to do up-front.
            if (!value.SkipSchemaGeneration)
            {
                foreach (var subClass in value.SubClasses)
                {
                    _mappings.Swap(d => d.AddOrUpdate(subClass.DocumentType, subClass));
                    _features.Swap(d => d.AddOrUpdate(subClass.DocumentType, subClass.Parent.Schema));
                }
            }
        }

        return value;
    }

    internal IDocumentMapping FindMapping(Type documentType)
    {
        ArgumentNullException.ThrowIfNull(documentType);

        if (!_mappings.Value.TryFind(documentType, out var value))
        {
            // 9.0: BuildAllMappings is no longer eager (#4303), so a subclass
            // looked up on its own (without the parent ever being touched)
            // wouldn't show up in AllDocumentMappings. Walk the type chain
            // and the implemented-interfaces set, force-materializing any
            // registered ancestor so its SubClasses declarations populate.
            // This is targeted: we only materialize candidates that the
            // queried type actually inherits from, not every builder.
            for (var ancestor = documentType.BaseType;
                 ancestor != null && ancestor != typeof(object);
                 ancestor = ancestor.BaseType)
            {
                if (_builders.Value.TryFind(ancestor, out _))
                {
                    MappingFor(ancestor);
                }
            }
            foreach (var iface in documentType.GetInterfaces())
            {
                if (_builders.Value.TryFind(iface, out _))
                {
                    MappingFor(iface);
                }
            }

            var subclass = AllDocumentMappings.SelectMany(x => x.SubClasses)
                .FirstOrDefault(x => x.DocumentType == documentType) as IDocumentMapping;

            value = subclass ?? MappingFor(documentType);
            _mappings.Swap(d => d.AddOrUpdate(documentType, value));

            assertNoDuplicateDocumentAliases();
        }

        return value;
    }

    internal void AddMapping(IDocumentMapping mapping)
    {
        _mappings.Swap(d => d.AddOrUpdate(mapping.DocumentType, mapping));
    }


    private void assertNoDuplicateDocumentAliases()
    {
        var duplicates =
            AllDocumentMappings.Where(x => !x.StructuralTyped)
                .GroupBy(x => $"{x.DatabaseSchemaName}.{x.Alias}")
                .Where(x => x.Count() > 1)
                .ToArray();

        if (duplicates.Length != 0)
        {
            var message = duplicates
                    // We are making it legal to use the same document alias across different schemas
                .Select(group =>
            {
                return
                    $"Document types {group.Select(x => x.DocumentType.FullName!).Join(", ")} all have the same document alias '{group.Key}'. You must explicitly make document type aliases to disambiguate the database schema objects";
            }).Join("\n");

            throw new AmbiguousDocumentTypeAliasesException(message);
        }
    }

    /// <summary>
    ///     Retrieve an IFeatureSchema for the designated type
    /// </summary>
    /// <param name="featureType"></param>
    /// <returns></returns>
    public IFeatureSchema FindFeature(Type featureType)
    {
        if (_features.Value.TryFind(featureType, out var schema))
        {
            return schema;
        }

        if (_options.EventGraph.AllEvents().Any(x => x.DocumentType == featureType))
        {
            return _options.EventGraph;
        }

        if (featureType == typeof(StorageFeatures))
        {
            return this;
        }

        // 9.0: With lazy mapping materialization (#4303) a subclass-only
        // feature lookup (e.g. EnsureStorageExistsAsync(typeof(SuperUser))
        // when User is the registered parent) can't be served from
        // _features yet. Route through FindMapping so its type-chain walk
        // materializes the parent, populates _features for the subclass,
        // and we can complete the lookup correctly. If that still doesn't
        // produce a feature entry the type is genuinely a top-level
        // mapping and MappingFor.Schema is the right answer.
        FindMapping(featureType);
        if (_features.Value.TryFind(featureType, out schema))
        {
            return schema;
        }

        return MappingFor(featureType).Schema;
    }

    internal void PostProcessConfiguration()
    {
        if (_options.Advanced.UseNGramSearchWithUnaccent)
            ExtendedSchemaObjects.Add(new Extension("unaccent"));

        SystemFunctions.AddSystemFunction(_options, "mt_immutable_timestamp", "text");
        SystemFunctions.AddSystemFunction(_options, "mt_immutable_timestamptz", "text");
        SystemFunctions.AddSystemFunction(_options, "mt_immutable_time", "text");
        SystemFunctions.AddSystemFunction(_options, "mt_immutable_date", "text");
        SystemFunctions.AddSystemFunction(_options, "mt_grams_vector", "text,boolean");
        SystemFunctions.AddSystemFunction(_options, "mt_grams_query", "text,boolean");
        SystemFunctions.AddSystemFunction(_options, "mt_grams_array", "text,boolean");
        SystemFunctions.AddSystemFunction(_options, "mt_jsonb_append", "jsonb,text[],jsonb,boolean,jsonb");
        SystemFunctions.AddSystemFunction(_options, "mt_jsonb_append_key_value", "jsonb,text[],jsonb,boolean");
        SystemFunctions.AddSystemFunction(_options, "mt_jsonb_copy", "jsonb,text[],text[]");
        SystemFunctions.AddSystemFunction(_options, "mt_jsonb_duplicate", "jsonb,text[],jsonb");
        SystemFunctions.AddSystemFunction(_options, "mt_jsonb_fix_null_parent", "jsonb,text[]");
        SystemFunctions.AddSystemFunction(_options, "mt_jsonb_increment", "jsonb,text[],numeric");
        SystemFunctions.AddSystemFunction(_options, "mt_jsonb_insert", "jsonb,text[],jsonb,integer,boolean");
        SystemFunctions.AddSystemFunction(_options, "mt_jsonb_move", "jsonb,text[],text");
        SystemFunctions.AddSystemFunction(_options, "mt_jsonb_path_to_array", "text,char(1)");
        SystemFunctions.AddSystemFunction(_options, "mt_jsonb_remove", "jsonb,text[],jsonb");
        SystemFunctions.AddSystemFunction(_options, "mt_jsonb_remove_key", "jsonb,text[],text");
        SystemFunctions.AddSystemFunction(_options, "mt_jsonb_patch", "jsonb,jsonb");
        SystemFunctions.AddSystemFunction(_options, "mt_safe_unaccent", "boolean,text");

        Add(SystemFunctions);

        Add(_options.EventGraph);


        _features.Swap(d => d.AddOrUpdate(typeof(StreamState), _options.EventGraph));
        _features.Swap(d => d.AddOrUpdate(typeof(StreamAction), _options.EventGraph));
        _features.Swap(d => d.AddOrUpdate(typeof(IEvent), _options.EventGraph));

        _mappings.Swap(d => d.AddOrUpdate(typeof(IEvent), new EventQueryMapping(_options)));

        foreach (var mapping in DocumentMappingsWithSchema)
        {
            foreach (var subClass in mapping.SubClasses)
            {
                _mappings.Swap(d => d.AddOrUpdate(subClass.DocumentType, subClass));
                _features.Swap(d => d.AddOrUpdate(subClass.DocumentType, subClass.Parent.Schema));
            }
        }
    }

    internal IEnumerable<IFeatureSchema> AllActiveFeatures(IMartenDatabase database)
    {
        // 9.0: AllActiveFeatures is the entry point for full-schema operations
        // (ApplyAllConfiguredChangesToDatabaseAsync, the CLI tools). Force every
        // registered document type to materialize before enumerating
        // DocumentMappingsWithSchema so we don't silently miss types after #4303
        // moved BuildAllMappings off the cold-start path.
        BuildAllMappings();

        yield return SystemFunctions;

        if (_options.Events.As<EventGraph>().IsActive(_options))
        {
            MappingFor(typeof(DeadLetterEvent)).DatabaseSchemaName = _options.Events.DatabaseSchemaName;
        }

        var mappings = DocumentMappingsWithSchema
            .OrderBy(x => x.DocumentType.Name)
            .TopologicalSort(m => m.ReferencedTypes()
                .Select(MappingFor));

        if (ExtendedSchemaObjects.Any())
        {
            yield return this;
        }

        foreach (var mapping in mappings) yield return mapping.Schema;

        if (SequenceIsRequired())
        {
            yield return database.Sequences;
        }

        if (_options.Events.As<EventGraph>().IsActive(_options))
        {
            yield return _options.EventGraph;
        }

        // Features explicitly registered via Storage.Add() that aren't one of Marten's built-ins already
        // yielded above. The assembly check is the proxy for "built-in": SystemFunctions, the EventGraph,
        // and every document mapping schema live in the Marten assembly and are yielded directly above, so
        // they must not be re-emitted here (a double-yield reruns their DDL — e.g. "column already exists").
        // A feature FOLDED INTO the Marten assembly but contributed by an opt-in extension carries the
        // IExtensionFeatureSchema marker (e.g. TimescaleDBFeatureSchema, #4980) so it is still yielded here,
        // after the document/event tables, so its create_hypertable / continuous-aggregate DDL lands once the
        // underlying tables exist.
        var custom = _features.Value.ToArray().Select(e => e.Value)
            .Where(x => x.GetType().Assembly != GetType().Assembly || x is IExtensionFeatureSchema).ToArray();

        foreach (var featureSchema in custom) yield return featureSchema;

        // #4863/#4855: the Marten-managed tenant partition registry (mt_tenant_partitions) is
        // registered as a feature, but its type — DatabaseScopedTenantPartitions — now lives in the
        // Marten assembly, so the assembly-based `custom` filter above excludes it (the plain Weasel
        // ManagedListPartitions it replaced was in the Weasel assembly and so slipped through). Yield
        // it explicitly so the registry table is materialized on EVERY database of the store during a
        // full apply, not only on databases that happen to get an explicit provisioning touch. Under
        // sharded tenancy the projection coordinator reads each shard's own mt_tenant_partitions to
        // expand per-tenant agents; a shard that never had that table created would fail every
        // leadership polling cycle with 42P01. Guard against a double-yield in case a future partition
        // manager type lands back in a non-Marten assembly (then `custom` would already include it).
        var tenantPartitions = _options.TenantPartitions?.Partitions;
        if (tenantPartitions != null && !custom.Contains(tenantPartitions))
        {
            yield return tenantPartitions;
        }
    }

    internal bool SequenceIsRequired()
    {
        return DocumentMappingsWithSchema.Any(x => x.IdStrategy.IsNumeric);
    }

    internal IEnumerable<Type> GetTypeDependencies(Type type)
    {
        if (_typeDependencies.TryFind(type, out var deps))
        {
            return deps;
        }

        deps = determineTypeDependencies(type);
        _typeDependencies = _typeDependencies.AddOrUpdate(type, deps);

        return deps;
    }

    private IEnumerable<Type> determineTypeDependencies(Type type)
    {
        if (type == typeof(StorageFeatures)) return Array.Empty<Type>();

        var mapping = FindMapping(type);
        var documentMapping = mapping as DocumentMapping ?? (mapping as SubClassMapping)?.Parent;
        if (documentMapping == null)
        {
            return Enumerable.Empty<Type>();
        }


        return documentMapping.ReferencedTypes()
            .SelectMany(keyDefinition =>
            {
                var results = new List<Type>();
                // If the reference type has sub-classes, also need to insert/update them first too
                if (FindMapping(keyDefinition) is DocumentMapping referenceMappingType &&
                    referenceMappingType.SubClasses.Any())
                {
                    results.AddRange(referenceMappingType.SubClasses.Select(s => s.DocumentType));
                }

                results.Add(keyDefinition);
                return results;
            });
    }


    /// <summary>
    ///     Used to support MartenRegistry.Include()
    /// </summary>
    /// <param name="includedStorage"></param>
    internal void IncludeDocumentMappingBuilders(StorageFeatures includedStorage)
    {
        foreach (var builder in includedStorage._builders.Value.ToArray().Select(x => x.Value))
        {
            if (_builders.Value.TryFind(builder.DocumentType, out var existing))
            {
                existing.Include(builder);
            }
            else
            {
                _builders.Swap(d => d.AddOrUpdate(builder.DocumentType, builder));
            }
        }
    }

    OptionsDescription IDescribeMyself.ToDescription()
    {
        var description = new OptionsDescription(this);
        foreach (var mapping in AllDocumentMappings)
        {
            description.Children[mapping.DocumentType.FullNameInCode()] = new OptionsDescription(mapping);
        }

        return description;
    }
}
