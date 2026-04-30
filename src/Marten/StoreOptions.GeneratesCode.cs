using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using JasperFx.Descriptors;
using Marten.Internal.CodeGeneration;
using Marten.Schema;
using Microsoft.Extensions.Hosting;

namespace Marten;

public partial class StoreOptions: ICodeFileCollection
{
    internal const string PreferJasperFxMessage =
        "Prefer using the IServiceCollection.CritterStackDefaults() API to override code generation and AutoCreate configuration across all JasperFx/Critter Stack tools. This API will be removed in Marten 9";

    /// <summary>
    ///     The main application assembly. By default this is the entry assembly for the application,
    ///     but you may need to change this in testing scenarios
    /// </summary>
    [Obsolete(PreferJasperFxMessage)]
    public Assembly ApplicationAssembly { get; set; }

    private bool? _sourceCodeWritingEnabled;

    [Obsolete(PreferJasperFxMessage)]
    public bool SourceCodeWritingEnabled
    {
        get
        {
            return _sourceCodeWritingEnabled ?? true;
        }
        set
        {
            _sourceCodeWritingEnabled = value;
        }
    }

    // This would only be set for "additional" document stores
    public string StoreName { get; set; } = "Main";

    /// <summary>
    ///     Root folder where generated code should be placed. By default, this is the IHostEnvironment.ContentRootPath
    /// </summary>
    [Obsolete(PreferJasperFxMessage)]
    [IgnoreDescription]
    public string GeneratedCodeOutputPath { get; set; }

    public IReadOnlyList<ICodeFile> BuildFiles()
    {
        Storage.BuildAllMappings();
        return Storage
            .AllDocumentMappings
            .Select(x => new DocumentProviderBuilder(x, this))
            .ToList();
    }

    [IgnoreDescription]
    GenerationRules ICodeFileCollection.Rules => CreateGenerationRules();

    string ICodeFileCollection.ChildNamespace { get; } = "DocumentStorage";

    // 9.0: cache the base GenerationRules so we don't redo Path.Combine,
    // Assembly.GetEntryAssembly, and the two ReferenceAssembly walks on
    // every CreateGenerationRules() call (#4307). Callers that need to
    // ReferenceTypes(...) for a specific document type get a Clone() so
    // the mutation stays scoped to their compile and doesn't accumulate
    // on the cached base.
    private GenerationRules? _cachedBaseRules;
    private readonly object _cachedRulesLock = new();

    internal GenerationRules CreateGenerationRules()
    {
        return GetCachedBaseRules().Clone();
    }

    private GenerationRules GetCachedBaseRules()
    {
        if (_cachedBaseRules is { } existing)
        {
            return existing;
        }

        lock (_cachedRulesLock)
        {
            if (_cachedBaseRules is { } existing2)
            {
                return existing2;
            }

            var rules = new GenerationRules(SchemaConstants.MartenGeneratedNamespace)
            {
                TypeLoadMode = GeneratedCodeMode,
                GeneratedCodeOutputPath = GeneratedCodeOutputPath ?? AppContext.BaseDirectory
                    .AppendPath("Internal", "Generated"),
                ApplicationAssembly = ApplicationAssembly ?? Assembly.GetEntryAssembly(),
                SourceCodeWritingEnabled = SourceCodeWritingEnabled
            };

            if (StoreName.IsNotEmpty() && StoreName != "Marten" && StoreName != "Main")
            {
                rules.GeneratedNamespace += "." + StoreName;
                rules.GeneratedCodeOutputPath = Path.Combine(rules.GeneratedCodeOutputPath, StoreName);
            }

            rules.ReferenceAssembly(GetType().Assembly);
            rules.ReferenceAssembly(Assembly.GetEntryAssembly()!);

            _cachedBaseRules = rules;
            return rules;
        }
    }

    internal void ReadJasperFxOptions(JasperFxOptions? options)
    {
        if (options == null) return;

        if (!_tenantIdStyle.HasValue)
        {
            _tenantIdStyle = options.TenantIdStyle;
        }

        ApplicationAssembly ??= options.ApplicationAssembly;
        GeneratedCodeOutputPath ??= options.GeneratedCodeOutputPath;
        _generatedCodeMode ??= options.ActiveProfile.GeneratedCodeMode;
        _autoCreate ??= options.ActiveProfile.ResourceAutoCreate;
        _sourceCodeWritingEnabled ??= options.ActiveProfile.SourceCodeWritingEnabled;
    }
}
