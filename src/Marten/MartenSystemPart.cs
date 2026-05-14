using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.CommandLine;
using JasperFx.CommandLine.Descriptions;
using JasperFx.Descriptors;
using JasperFx.Resources;
using Weasel.Core.CommandLine;
using System.Diagnostics.CodeAnalysis;

namespace Marten;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("Trimming", "IL2046",
    Justification = "Class-level: override RUC mismatch: base method does not yet carry RequiresUnreferencedCode. Suppressed locally; long-term fix is to annotate the base method.")]
internal class MartenSystemPart : SystemPartBase
{
    public static Uri MartenStoreUri { get; } = new Uri("marten://store");

    private readonly IDocumentStore _store;

    protected MartenSystemPart(IDocumentStore store, string title, Uri subjectUri) : base(title, subjectUri)
    {
        _store = store;
    }

    public MartenSystemPart(IDocumentStore store) : this(store, "Marten", MartenStoreUri)
    {

    }

    public override Task WriteToConsole()
    {
        var description = OptionsDescription.For(_store);
        OptionDescriptionWriter.Write(description);
        return Task.CompletedTask;
    }

    public override async ValueTask<IReadOnlyList<IStatefulResource>> FindResources()
    {
        var databases = await _store.Options.Tenancy.BuildDatabases().ConfigureAwait(false);
        return databases.Select(x => new DatabaseResource(x, SubjectUri)).ToArray();
    }
}

internal class MartenSystemPart<T>: MartenSystemPart where T : IDocumentStore
{
    public MartenSystemPart(T store) : base(store, $"Marten {typeof(T).Name}", new Uri("marten://" + typeof(T).Name.ToLowerInvariant()))
    {
    }
}
