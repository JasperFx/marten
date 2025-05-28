using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using Marten.Internal.CodeGeneration;
using Marten.Schema;
using Microsoft.Extensions.Hosting;

namespace Marten;

public partial class StoreOptions: ICodeFileCollection
{
    internal const string PreferJasperFxMessage =
        "Prefer using the IServiceCollection.AddJasperFx() API to override code generation and AutoCreate configuration across all JasperFx/Critter Stack tools. This API will be removed in Marten 9";

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
    public string StoreName { get; set; } = "Marten";

    /// <summary>
    ///     Root folder where generated code should be placed. By default, this is the IHostEnvironment.ContentRootPath
    /// </summary>
    [Obsolete(PreferJasperFxMessage)]
    public string GeneratedCodeOutputPath { get; set; }

    public IReadOnlyList<ICodeFile> BuildFiles()
    {
        Storage.BuildAllMappings();
        return Storage
            .AllDocumentMappings
            .Select(x => new DocumentProviderBuilder(x, this))
            .ToList();
    }

    GenerationRules ICodeFileCollection.Rules => CreateGenerationRules();

    string ICodeFileCollection.ChildNamespace { get; } = "DocumentStorage";

    internal GenerationRules CreateGenerationRules()
    {
        var rules = new GenerationRules(SchemaConstants.MartenGeneratedNamespace)
        {
            TypeLoadMode = GeneratedCodeMode,
            GeneratedCodeOutputPath = GeneratedCodeOutputPath ?? AppContext.BaseDirectory
                .AppendPath("Internal", "Generated"),
            ApplicationAssembly = ApplicationAssembly ?? Assembly.GetEntryAssembly(),
            SourceCodeWritingEnabled = SourceCodeWritingEnabled
        };

        if (StoreName.IsNotEmpty() && StoreName != "Marten")
        {
            rules.GeneratedNamespace += "." + StoreName;
            rules.GeneratedCodeOutputPath = Path.Combine(rules.GeneratedCodeOutputPath, StoreName);
        }

        rules.ReferenceAssembly(GetType().Assembly);
        rules.ReferenceAssembly(Assembly.GetEntryAssembly()!);

        return rules;
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
