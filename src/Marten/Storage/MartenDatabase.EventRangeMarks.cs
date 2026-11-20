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
/// jasperfx#1215 / #1222: the event-range marks on <see cref="IEventDatabase" />, over
/// <see cref="EventRangeMarkTable" />. A stored-event rewrite keeps its ceiling and its cursor here, and so will a
/// projection backfill.
/// </summary>
public partial class MartenDatabase
{
    /// <summary>
    /// The mark stored under <paramref name="key" /> for <paramref name="tenantId" /> (null for store-global), or null
    /// when none is stored.
    /// </summary>
    public async Task<long?> FetchEventRangeMarkAsync(string key, string? tenantId, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        await using var conn = CreateConnection();
        try
        {
            await conn.OpenAsync(token).ConfigureAwait(false);
            return await selectEventRangeMarkAsync(conn, null, key, markTenant(tenantId), token)
                .ConfigureAwait(false);
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Store <paramref name="mark" /> only if no mark is stored under <paramref name="key" /> yet, and return the mark
    /// that is stored afterwards: the winner of any race.
    /// </summary>
    /// <remarks>
    /// Two statements on purpose, for the reason given on <see cref="TrySetProjectionFloorAsync" />: the separate
    /// <c>select</c> sees the row a concurrent inserter committed while the <c>insert</c> waited on it.
    /// </remarks>
    public async Task<long> TrySetEventRangeMarkAsync(string key, string? tenantId, long mark,
        CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        var tenant = markTenant(tenantId);
        await using var conn = CreateConnection();
        try
        {
            await conn.OpenAsync(token).ConfigureAwait(false);

            await using (var insert = conn.CreateCommand(
                             $"insert into {markTable} (name, tenant_id, mark) values (:name, :tenant_id, :mark) on conflict (name, tenant_id) do nothing"))
            {
                bindMarkKey(insert, key, tenant).With("mark", mark);
                await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            return await selectEventRangeMarkAsync(conn, null, key, tenant, token).ConfigureAwait(false)
                   ?? throw new InvalidOperationException(
                       $"The event-range mark '{key}' was cleared while it was being stored. Try again.");
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Set the mark under <paramref name="key" /> unconditionally, or clear it with a null <paramref name="mark" />.
    /// </summary>
    public async Task SetEventRangeMarkAsync(string key, string? tenantId, long? mark,
        CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        await using var conn = CreateConnection();
        try
        {
            await conn.OpenAsync(token).ConfigureAwait(false);
            await setEventRangeMarkAsync(conn, null, key, tenantId, mark, token).ConfigureAwait(false);
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Set or clear a mark on a connection the caller holds, inside its transaction when it passes one. The rewrite
    /// writes each page's cursor this way, in the page's own transaction.
    /// </summary>
    internal Task SetEventRangeMarkAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, string key, string? tenantId,
        long? mark, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return setEventRangeMarkAsync(conn, tx, key, tenantId, mark, token);
    }

    private async Task setEventRangeMarkAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, string key,
        string? tenantId, long? mark, CancellationToken token)
    {
        await using var command = mark.HasValue
            ? conn.CreateCommand(
                    $"insert into {markTable} (name, tenant_id, mark) values (:name, :tenant_id, :mark) on conflict (name, tenant_id) do update set mark = excluded.mark, last_updated = transaction_timestamp()",
                    tx)
                .With("mark", mark.Value)
            : conn.CreateCommand($"delete from {markTable} where name = :name and tenant_id = :tenant_id", tx);

        bindMarkKey(command, key, markTenant(tenantId));
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    /// <summary>
    /// The highest event sequence of one tenant, which is where a rewrite of that tenant stops when it is first
    /// requested. Under per-tenant event partitioning each tenant draws its own sequence, so this reads the
    /// tenant's own events. Otherwise the sequence is store-wide and this is
    /// <see cref="FetchHighestEventSequenceNumber(CancellationToken)" />.
    /// </summary>
    public async Task<long> FetchHighestEventSequenceNumber(string? tenantId, CancellationToken token)
    {
        if (tenantId == null || !Options.Events.UseTenantPartitionedEvents)
        {
            return await FetchHighestEventSequenceNumber(token).ConfigureAwait(false);
        }

        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        await using var conn = CreateConnection();
        try
        {
            await conn.OpenAsync(token).ConfigureAwait(false);
            var result = await conn
                .CreateCommand(
                    $"select coalesce(max(seq_id), 0) from {Options.Events.DatabaseSchemaName}.mt_events where tenant_id = :tenant_id")
                .With("tenant_id", Options.TenantIdStyle.MaybeCorrectTenantId(tenantId), NpgsqlDbType.Varchar)
                .ExecuteScalarAsync(token).ConfigureAwait(false);

            return Convert.ToInt64(result);
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    private string markTable => $"{Options.EventGraph.DatabaseSchemaName}.{EventRangeMarkTable.Name}";

    /// <summary>
    /// Null is a store-global mark, stored under <see cref="EventRangeMarkTable.GlobalTenant" /> because
    /// <c>tenant_id</c> is part of the primary key. A real tenant id is corrected like the floors' tenants.
    /// </summary>
    private string markTenant(string? tenantId)
        => tenantId == null ? EventRangeMarkTable.GlobalTenant : Options.TenantIdStyle.MaybeCorrectTenantId(tenantId);

    private static NpgsqlCommand bindMarkKey(NpgsqlCommand command, string key, string tenant)
        => command.With("name", key, NpgsqlDbType.Varchar).With("tenant_id", tenant, NpgsqlDbType.Varchar);

    private async Task<long?> selectEventRangeMarkAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, string key,
        string tenant, CancellationToken token)
    {
        await using var select =
            conn.CreateCommand($"select mark from {markTable} where name = :name and tenant_id = :tenant_id", tx);
        bindMarkKey(select, key, tenant);

        var result = await select.ExecuteScalarAsync(token).ConfigureAwait(false);
        return result is null or DBNull ? null : Convert.ToInt64(result);
    }
}
