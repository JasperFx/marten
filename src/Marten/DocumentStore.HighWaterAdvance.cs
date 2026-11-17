using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon.HighWater;
using JasperFx.MultiTenancy;
using Marten.Events.Daemon.HighWater;
using Marten.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Marten;

/// <summary>
///     #5738 / jasperfx#1031: the store-agnostic preview and advance of a high water mark, which is what lets
///     CritterWatch jump-start a daemon held behind a gap without compiling against Marten.
/// </summary>
public partial class DocumentStore
{
    async Task<IReadOnlyList<HighWaterMarkPreview>> IEventStore.PreviewHighWaterAdvanceAsync(
        IEventDatabase database, string? tenantId, CancellationToken token)
    {
        var db = (MartenDatabase)requireMartenDatabase(database);
        var detector = await highWaterDetectorFor(db, token).ConfigureAwait(false);

        var results = new List<HighWaterMarkPreview>();
        foreach (var markTenant in await highWaterMarksFor(db, tenantId, token).ConfigureAwait(false))
        {
            results.Add(await detector.PreviewAdvanceToCommittedCeilingAsync(markTenant, token).ConfigureAwait(false));
        }

        return results;
    }

    async Task<IReadOnlyList<HighWaterMarkAdvance>> IEventStore.AdvanceHighWaterMarkToLatestAsync(
        IEventDatabase database, string? tenantId, long? expectedCurrentMark, CancellationToken token)
    {
        var db = (MartenDatabase)requireMartenDatabase(database);

        // A compare-and-set guards ONE mark. A null tenant on a per-tenant database selects every tenant's,
        // and one expected value cannot describe them all.
        if (expectedCurrentMark.HasValue && tenantId == null && Options.Events.UseTenantPartitionedEvents)
        {
            throw new ArgumentException(
                "An expected current mark guards a single high water mark, but this database keeps one mark per " +
                "tenant (per-tenant partitioned events) and no tenant was named, which selects every tenant's. " +
                "Name the tenant.", nameof(expectedCurrentMark));
        }

        var marks = await highWaterMarksFor(db, tenantId, token).ConfigureAwait(false);
        return await AdvanceHighWaterMarksAsync(db, marks, expectedCurrentMark, token).ConfigureAwait(false);
    }

    /// <summary>
    ///     Shared by the <see cref="IEventStore" /> members and by <c>Advanced.AdvanceHighWaterMarkToLatestAsync</c>.
    ///     Each entry of <paramref name="marks" /> is null for the store-global mark or a tenant id for that
    ///     tenant's own mark.
    /// </summary>
    internal async Task<IReadOnlyList<HighWaterMarkAdvance>> AdvanceHighWaterMarksAsync(MartenDatabase database,
        IReadOnlyList<string?> marks, long? expectedCurrentMark, CancellationToken token)
    {
        var detector = await highWaterDetectorFor(database, token).ConfigureAwait(false);

        var results = new List<HighWaterMarkAdvance>(marks.Count);
        foreach (var markTenant in marks)
        {
            results.Add(await detector.AdvanceToCommittedCeilingAsync(markTenant, expectedCurrentMark, token)
                .ConfigureAwait(false));
        }

        return results;
    }

    /// <summary>
    ///     The marks a tenant id selects, per jasperfx#1031. Without per-tenant partitioned events the database
    ///     keeps ONE mark that is every tenant's, so a named tenant is refused rather than quietly advancing all
    ///     of them. With it, the store-global mark means nothing (each tenant draws its own sequence), so a null
    ///     tenant selects every tenant's own mark.
    /// </summary>
    private async Task<IReadOnlyList<string?>> highWaterMarksFor(MartenDatabase database, string? tenantId,
        CancellationToken token)
    {
        if (!Options.Events.UseTenantPartitionedEvents)
        {
            if (tenantId != null)
            {
                throw new ArgumentException(
                    $"Database '{database.Identifier}' keeps one high water mark for every tenant, because this " +
                    $"store does not use per-tenant partitioned events, so tenant '{tenantId}' has no mark of its " +
                    "own. Pass a null tenant to preview or advance the database's mark.", nameof(tenantId));
            }

            return [null];
        }

        if (tenantId != null)
        {
            return [Options.TenantIdStyle.MaybeCorrectTenantId(tenantId)];
        }

        return await managedTenantMarksAsync(database, token).ConfigureAwait(false);
    }

    internal static async Task<IReadOnlyList<string?>> managedTenantMarksAsync(MartenDatabase database,
        CancellationToken token)
    {
        try
        {
            return [.. await database.FetchManagedTenantIdsAsync(token).ConfigureAwait(false)];
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // No tenant has ever been provisioned here, so there are no per-tenant marks to touch
            return [];
        }
    }

    private async Task<HighWaterDetector> highWaterDetectorFor(MartenDatabase database, CancellationToken token)
    {
        await database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);
        return new HighWaterDetector(database, Options.EventGraph, NullLogger.Instance);
    }
}
