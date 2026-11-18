#nullable enable
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Schema;

/// <summary>
///     Serialises provisioning of Marten-managed tenant partitions in one database, so concurrent onboarding of a
///     tenant waits for the first one instead of racing it.
/// </summary>
/// <remarks>
///     <para>
///         Two callers creating the same partition or per-tenant sequence at once both run
///         <c>CREATE ... IF NOT EXISTS</c>, and PostgreSQL can still fail one of them with 23505 on
///         <c>pg_type_typname_nsp_index</c>. Weasel retries that a few times, which is not enough when several
///         callers onboard the same tenant together (#4596's
///         <c>concurrent_AddMartenManagedTenantsAsync_for_same_tenant_is_idempotent</c>). Holding this lock
///         around the partitions and the sequences makes the later callers find everything already there.
///     </para>
///     <para>
///         It is a session-level advisory lock on a connection of its own, taken with the two-key form
///         <c>pg_advisory_lock(int, int)</c>. That form is a separate key space from the single bigint keys the
///         async daemon, the gap liveness probe, <c>ApplyChangesLockId</c> and sharded tenancy use, so it cannot
///         collide with any of them. Advisory locks are per database, and the second key is derived from the
///         schema, so two stores in different schemas of one database do not wait on each other. The connection
///         is not inside a transaction while it waits or holds, so the gap liveness probe never counts it.
///         Disposing releases the lock; if that fails, the connection is gone and the server has released it.
///     </para>
/// </remarks>
internal sealed class TenantProvisioningLock: IAsyncDisposable
{
    /// <summary>The first key of the two-key form: the same for every store.</summary>
    internal const int Namespace = 4596;

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly NpgsqlConnection _connection;
    private readonly int _key;

    private TenantProvisioningLock(NpgsqlConnection connection, int key)
    {
        _connection = connection;
        _key = key;
    }

    /// <summary>
    ///     The second key: a stable FNV-1a hash of the schema name, the same in every process and on every run.
    /// </summary>
    internal static int KeyFor(string schemaName)
    {
        unchecked
        {
            var hash = 2166136261;
            foreach (var b in Encoding.UTF8.GetBytes(schemaName.ToLowerInvariant()))
            {
                hash = (hash ^ b) * 16777619;
            }

            return (int)hash;
        }
    }

    /// <summary>
    ///     Wait until this database's provisioning lock for <paramref name="schemaName" /> is free, then hold it until
    ///     the result is disposed. Polls with <c>pg_try_advisory_lock</c> rather than blocking in
    ///     <c>pg_advisory_lock</c>, so a long provisioning pass elsewhere cannot run the wait into a command timeout,
    ///     and cancelling <paramref name="token" /> stops the wait.
    /// </summary>
    public static async Task<TenantProvisioningLock> AcquireAsync(PostgresqlDatabase database, string schemaName,
        CancellationToken token)
    {
        var key = KeyFor(schemaName);
        var connection = database.CreateConnection();

        try
        {
            await connection.OpenAsync(token).ConfigureAwait(false);

            while (true)
            {
                await using var cmd = new NpgsqlCommand("select pg_try_advisory_lock(@namespace, @key)", connection);
                cmd.Parameters.AddWithValue("namespace", Namespace);
                cmd.Parameters.AddWithValue("key", key);

                if (await cmd.ExecuteScalarAsync(token).ConfigureAwait(false) is true)
                {
                    return new TenantProvisioningLock(connection, key);
                }

                await Task.Delay(PollInterval, token).ConfigureAwait(false);
            }
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var cmd = new NpgsqlCommand("select pg_advisory_unlock(@namespace, @key)", _connection);
            cmd.Parameters.AddWithValue("namespace", Namespace);
            cmd.Parameters.AddWithValue("key", _key);
            await cmd.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
