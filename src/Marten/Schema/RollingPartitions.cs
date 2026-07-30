#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Weasel.Postgresql.Tables.Partitioning;

namespace Marten.Schema;

/// <summary>
/// Drives the rolling time-window RANGE partitions configured through
/// <see cref="PartitioningExpression.ByRollingRange(Weasel.Core.Partitioning.PartitionPeriod,int,int,System.TimeProvider)"/>
/// (marten#5093, built on weasel#401).
///
/// <para>
/// Provisioning at the leading edge is already covered by ordinary schema migration: with a partition
/// manager attached, <c>RangePartitioning.CreateDelta</c> is purely additive, so a window that has rolled
/// forward diffs as "create the new partition" rather than as a rebuild of a multi-gigabyte table. What
/// migration deliberately does NOT do is remove data, so retiring the aged partitions at the trailing edge
/// has to be driven explicitly — that is what this type is for, and it is why the maintenance pass runs
/// alongside the startup schema application rather than inside it.
/// </para>
///
/// <para>
/// The managers are discovered from the database's own schema objects rather than tracked in
/// <see cref="StoreOptions"/>, so this stays correct for any table that grows a rolling window later, and
/// re-running it is always idempotent.
/// </para>
/// </summary>
internal static class RollingPartitions
{
    /// <summary>
    /// Every distinct rolling-window partition manager attached to a table of this database. Reference
    /// identity is the key: <see cref="ManagedRangePartitions.ResolveManagedTables"/> matches tables to
    /// their manager the same way, so one manager shared across several document types rolls all of their
    /// tables forward in a single pass.
    /// </summary>
    public static IReadOnlyList<ManagedRangePartitions> ManagersFor(PostgresqlDatabase database)
    {
        var managers = new List<ManagedRangePartitions>();

        foreach (var table in database.AllObjects().OfType<Table>())
        {
            if (table.Partitioning is RangePartitioning { PartitionManager: ManagedRangePartitions manager }
                && !managers.Any(x => ReferenceEquals(x, manager)))
            {
                managers.Add(manager);
            }
        }

        return managers;
    }

    /// <summary>
    /// Run the maintenance pass over every database.
    /// </summary>
    /// <param name="rollForward">Create the partitions of the current window that do not exist yet.</param>
    /// <param name="dropAged">
    /// Drop the partitions that have fallen below the policy's retention floor. This removes data by design
    /// — dropping the partition is what reclaims the storage in O(1).
    /// </param>
    public static async Task<TablePartitionStatus[]> ApplyAsync(IEnumerable<PostgresqlDatabase> databases,
        ILogger logger, bool rollForward, bool dropAged, CancellationToken token)
    {
        var statuses = new List<TablePartitionStatus>();

        foreach (var database in databases)
        {
            foreach (var manager in ManagersFor(database))
            {
                var results = (rollForward, dropAged) switch
                {
                    (true, true) => await manager.ApplyAsync(database, logger, token).ConfigureAwait(false),
                    (true, false) => await manager.RollForwardAsync(database, logger, token).ConfigureAwait(false),
                    (false, true) => await manager.DropAgedPartitionsAsync(database, logger, token)
                        .ConfigureAwait(false),
                    _ => []
                };

                statuses.AddRange(results);
            }
        }

        return statuses.ToArray();
    }
}
