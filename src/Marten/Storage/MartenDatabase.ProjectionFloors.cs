#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.MultiTenancy;
using Marten.Events.Schema;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Storage;

/// <summary>
/// jasperfx#809 (#5740): the persisted projection start floor seam on <see cref="IEventDatabase" />, over
/// <see cref="ProjectionFloorTable" />. What makes <c>AsyncOptions.SubscribeFromFirstRun()</c> work on Marten,
/// and what lets an operator fix one database's floor without a deploy.
/// </summary>
public partial class MartenDatabase
{
    /// <summary>
    /// The stored floor, or null when none is stored. Called by the daemon on every continuous start and every
    /// rebuild, for every strategy but <c>FromPresent</c>.
    /// </summary>
    /// <remarks>
    /// A missing <c>mt_event_projection_floors</c> answers null rather than throwing (#5511). This read is on
    /// the daemon's start path, so an event store whose schema has not been migrated since the table was added
    /// must keep starting its shards exactly as it did before, not fail every one of them with 42P01.
    /// </remarks>
    public async Task<long?> FetchProjectionFloorAsync(string projectionName, string? tenantId,
        CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectionName);

        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        await using var conn = CreateConnection();
        try
        {
            await conn.OpenAsync(token).ConfigureAwait(false);
            return await selectFloorAsync(conn, projectionName, floorTenant(tenantId), token).ConfigureAwait(false);
        }
        catch (Exception e) when (isMissingEventStorage(e))
        {
            logMissingEventStorage(e, nameof(FetchProjectionFloorAsync));
            return null;
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Store <paramref name="floor" /> only if no floor is stored yet, and return the floor that is stored
    /// afterwards: the winner of any race between nodes starting the projection for the first time.
    /// </summary>
    /// <remarks>
    /// Two statements on purpose, not one. <c>insert ... on conflict do nothing</c> waits for a concurrent
    /// inserter of the same key to commit and then does nothing; a <c>select</c> in that same statement (a
    /// CTE, say) would still read from the snapshot taken before the wait and miss the winner's row. The
    /// separate <c>select</c> gets a fresh snapshot under READ COMMITTED, so every caller reads the one
    /// committed floor.
    /// </remarks>
    public async Task<long> TrySetProjectionFloorAsync(string projectionName, string? tenantId, long floor,
        CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectionName);

        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        var tenant = floorTenant(tenantId);
        await using var conn = CreateConnection();
        try
        {
            await conn.OpenAsync(token).ConfigureAwait(false);

            await using (var insert = conn.CreateCommand(
                             $"insert into {floorTable} (name, tenant_id, floor) values (:name, :tenant_id, :floor) on conflict (name, tenant_id) do nothing"))
            {
                bindKey(insert, projectionName, tenant).With("floor", floor);
                await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            return await selectFloorAsync(conn, projectionName, tenant, token).ConfigureAwait(false)
                   ?? throw new InvalidOperationException(
                       $"The start floor of projection '{projectionName}' was cleared while it was being pinned. Retry the start.");
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The operator's override: set the floor unconditionally, or clear it with a null
    /// <paramref name="floor" />. Reprocesses nothing by itself; the next start or rebuild honours it.
    /// </summary>
    public async Task SetProjectionFloorAsync(string projectionName, string? tenantId, long? floor,
        CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectionName);

        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        await using var conn = CreateConnection();
        try
        {
            await conn.OpenAsync(token).ConfigureAwait(false);

            await using var command = floor.HasValue
                ? conn.CreateCommand(
                        $"insert into {floorTable} (name, tenant_id, floor) values (:name, :tenant_id, :floor) on conflict (name, tenant_id) do update set floor = excluded.floor, last_updated = transaction_timestamp()")
                    .With("floor", floor.Value)
                : conn.CreateCommand($"delete from {floorTable} where name = :name and tenant_id = :tenant_id");

            bindKey(command, projectionName, floorTenant(tenantId));
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    private string floorTable => $"{Options.EventGraph.DatabaseSchemaName}.{ProjectionFloorTable.Name}";

    /// <summary>
    /// Null is a store-global floor, stored under <see cref="ProjectionFloorTable.GlobalTenant" /> because
    /// <c>tenant_id</c> is part of the primary key. A real tenant id is corrected the way every other tenant
    /// id on this surface is, so a floor set as "ACME" is found again as "acme" under a lower-casing style.
    /// </summary>
    private string floorTenant(string? tenantId)
        => tenantId == null ? ProjectionFloorTable.GlobalTenant : Options.TenantIdStyle.MaybeCorrectTenantId(tenantId);

    private static NpgsqlCommand bindKey(NpgsqlCommand command, string projectionName, string tenant)
        => command.With("name", projectionName, NpgsqlDbType.Varchar).With("tenant_id", tenant, NpgsqlDbType.Varchar);

    private async Task<long?> selectFloorAsync(NpgsqlConnection conn, string projectionName, string tenant,
        CancellationToken token)
    {
        await using var select =
            conn.CreateCommand($"select floor from {floorTable} where name = :name and tenant_id = :tenant_id");
        bindKey(select, projectionName, tenant);

        var result = await select.ExecuteScalarAsync(token).ConfigureAwait(false);
        return result is null or DBNull ? null : Convert.ToInt64(result);
    }
}
