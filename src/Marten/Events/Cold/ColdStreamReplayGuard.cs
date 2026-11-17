#nullable enable
using System.Threading;
using JasperFx;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten.Events.Schema;
using Marten.Exceptions;
using Marten.Storage;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Cold;

/// <summary>
///     The async daemon reads <c>mt_events</c> only, so a projection that replays history while streams are cold
///     would silently build read models without their cold part. Refuses that, unless the projection ignores
///     cold streams.
/// </summary>
internal static class ColdStreamReplayGuard
{
    /// <summary>
    ///     Whether the guard applies to this projection at all, without a database round trip.
    /// </summary>
    public static bool Applies(EventGraph events, AsyncOptions projectionOptions) =>
        events.ColdEventStore != null && !projectionOptions.IgnoresColdStreams();

    /// <param name="tenantId">Only this tenant's streams, for a per-tenant shard or rebuild; null for the whole database.</param>
    public static async Task AssertMayReplayAsync(EventGraph events, MartenDatabase database, string projectionName,
        AsyncOptions projectionOptions, string? tenantId, CancellationToken token)
    {
        if (!Applies(events, projectionOptions)) return;

        if (await hasColdStreamsAsync(events, database, tenantId, token).ConfigureAwait(false))
        {
            throw new ColdStreamReplayException(projectionName, database.Identifier, tenantId);
        }
    }

    private static async Task<bool> hasColdStreamsAsync(EventGraph events, MartenDatabase database, string? tenantId,
        CancellationToken token)
    {
        var filtersTenant = tenantId != null && events.TenancyStyle == TenancyStyle.Conjoined;

        var sql =
            $"select exists (select 1 from {events.DatabaseSchemaName}.{StreamsTable.TableName} where {StreamsTable.ColdVersionColumn} is not null" +
            (filtersTenant ? $" and {StorageConstants.TenantIdColumn} = :tenant" : "") + ")";

        await using var conn = database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        var command = conn.CreateCommand(sql);
        if (filtersTenant) command.With("tenant", tenantId!, NpgsqlDbType.Varchar);

        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is true;
    }
}
