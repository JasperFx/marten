using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.CommandLine;
using JasperFx.CommandLine.Descriptions;
using JasperFx.Descriptors;
using JasperFx.Resources;
using Weasel.Core.CommandLine;

namespace Marten;

internal class MartenSystemPart : SystemPartBase
{
    private readonly IDocumentStore _store;

    protected MartenSystemPart(IDocumentStore store, string title, Uri subjectUri) : base(title, subjectUri)
    {
        _store = store;
    }

    public MartenSystemPart(IDocumentStore store) : this(store, "Marten", new Uri("marten://documentstore"))
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
