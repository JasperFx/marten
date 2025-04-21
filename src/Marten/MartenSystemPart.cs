using System;
using JasperFx.CommandLine.Descriptions;

namespace Marten;

internal class MartenSystemPart : SystemPartBase
{
    private readonly IDocumentStore _store;

    public MartenSystemPart(IDocumentStore store) : base("Marten Store", new Uri("marten://main"))
    {
        _store = store;
    }
}

internal class MartenSystemPart<T>: MartenSystemPart where T : IDocumentStore
{
    public MartenSystemPart(T store) : base(store)
    {
    }
}
