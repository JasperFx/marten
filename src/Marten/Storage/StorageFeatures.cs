#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using ImTools;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Descriptors;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Exceptions;
using Marten.Schema;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;

namespace Marten.Storage;

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
        var m =  new DocumentMapping(type, options);
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
            // Just forcing them all to be built
            FindMapping(pair.Key);

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
            value = Build(documentType, _options);
            _documentMappings.Swap(d => d.AddOrUpdate(documentType, value));
        }

        return value;
    }

    internal IDocumentMapping FindMapping(Type documentType)
    {
        if (documentType == null)
        {
            throw new ArgumentNullException(nameof(documentType));
        }

        if (!_mappings.Value.TryFind(documentType, out var value))
        {
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

        if (duplicates.Any())
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
        SystemFunctions.AddSystemFunction(_options, "mt_jsonb_append", "jsonb,text[],jsonb,boolean");
        SystemFunctions.AddSystemFunction(_options, "mt_jsonb_copy", "jsonb,text[],text[]");
        SystemFunctions.AddSystemFunction(_options, "mt_jsonb_duplicate", "jsonb,text[],jsonb");
        SystemFunctions.AddSystemFunction(_options, "mt_jsonb_fix_null_parent", "jsonb,text[]");
        SystemFunctions.AddSystemFunction(_options, "mt_jsonb_increment", "jsonb,text[],numeric");
        SystemFunctions.AddSystemFunction(_options, "mt_jsonb_insert", "jsonb,text[],jsonb,integer,boolean");
        SystemFunctions.AddSystemFunction(_options, "mt_jsonb_move", "jsonb,text[],text");
        SystemFunctions.AddSystemFunction(_options, "mt_jsonb_path_to_array", "text,char(1)");
        SystemFunctions.AddSystemFunction(_options, "mt_jsonb_remove", "jsonb,text[],jsonb");
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

        var custom = _features.Value.ToArray().Select(e => e.Value)
            .Where(x => x.GetType().Assembly != GetType().Assembly).ToArray();

        foreach (var featureSchema in custom) yield return featureSchema;
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
