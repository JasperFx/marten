using System;
using JasperFx.Events;
using JasperFx.Events.Grouping;

namespace Marten.Events.Aggregation;

public static class AncillaryStoreEnrichmentExtensions
{
    /// <summary>
    /// Switch the enrichment source to an ancillary Marten store. The store is resolved
    /// lazily so projection construction does not deadlock DI. A lightweight session is
    /// opened per enrichment call and disposed automatically when enrichment completes.
    /// </summary>
    /// <example>
    /// await group.EnrichWith&lt;Tarief&gt;()
    ///     .UsingStore(_tarievenStore)
    ///     .ForEvent&lt;InvoiceCreated&gt;()
    ///     .ForEntityId(e => e.TariefId)
    ///     .Apply((slice, e, tarief) => e.Data.TariefNaam = tarief.Naam);
    /// </example>
    public static SliceGroup<TDoc, TId>.EntityStep<TEntity> UsingStore<TEntity, TStore, TDoc, TId>(
        this SliceGroup<TDoc, TId>.EntityStep<TEntity> step,
        Lazy<TStore> ancillaryStore)
        where TStore : IDocumentStore
        where TDoc : notnull
        where TId : notnull
    {
        var session = (IStorageOperations)ancillaryStore.Value.LightweightSession();
        return step.WithAlternateSession(session);
    }
}
