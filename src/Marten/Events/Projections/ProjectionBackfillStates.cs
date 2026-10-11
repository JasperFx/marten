using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events.Schema;
using Marten.Storage;
using Npgsql;

namespace Marten.Events.Projections;

/// <summary>
///     Where one backfill of one shard stands. <see cref="Name" /> is <c>{shard identity}:{backfill name}</c>.
/// </summary>
public record ProjectionBackfillState(string Name, long Position, long Ceiling, DateTimeOffset? Completed)
{
    public bool IsCompleted => Completed != null;
}

internal static class ProjectionBackfillStates
{
    /// <summary>
    ///     Every projection, composite members included, that declares at least one backfill.
    /// </summary>
    public static IReadOnlyList<IProjectionWithBackfills> Declaring(ProjectionOptions projections)
    {
        return sources(projections).OfType<IProjectionWithBackfills>().Where(x => x.HasBackfills).ToArray();
    }

    private static IEnumerable<object> sources(ProjectionOptions projections)
    {
        foreach (var source in projections.All)
        {
            yield return source;

            if (source is JasperFx.Events.Projections.Composite.CompositeProjection<IDocumentOperations, IQuerySession> composite)
            {
                foreach (var member in composite.AllProjections())
                {
                    yield return member is ICompositeProjectionServiceSource { SourceIfResolvable: { } resolved }
                        ? resolved
                        : member;
                }
            }
        }
    }

    /// <summary>
    ///     The backfill rows that must be completed before the shards of these projections are current. A null
    ///     tenant is the store-global shard.
    /// </summary>
    public static IReadOnlyList<string> ExpectedNames(IEnumerable<IProjectionWithBackfills> projections,
        IReadOnlyCollection<string?> tenants)
    {
        return projections
            .SelectMany(projection => tenants.SelectMany(tenant => projection.BackfillNames.Select(backfill =>
                $"{ShardName.Compose(projection.Name, tenantId: tenant, version: projection.Version).Identity}:{backfill}")))
            .ToArray();
    }

    public static async Task<IReadOnlyList<ProjectionBackfillState>> FetchAsync(IMartenDatabase database,
        CancellationToken token)
    {
        var options = database.As<MartenDatabase>().Options;
        if (Declaring(options.Projections).Count == 0)
        {
            return [];
        }

        await database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        await using var conn = database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        await using var cmd = new NpgsqlCommand(
            $"select name, position, ceiling, completed from {options.Events.DatabaseSchemaName}.{ProjectionBackfillTable.Name} order by name",
            conn);

        var states = new List<ProjectionBackfillState>();
        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            states.Add(new ProjectionBackfillState(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2),
                await reader.IsDBNullAsync(3, token).ConfigureAwait(false)
                    ? null
                    : await reader.GetFieldValueAsync<DateTimeOffset>(3, token).ConfigureAwait(false)));
        }

        return states;
    }

    /// <summary>
    ///     The expected backfills that have not completed yet, for the wait helpers.
    /// </summary>
    public static async Task<IReadOnlyList<string>> PendingAsync(IMartenDatabase database,
        IReadOnlyList<IProjectionWithBackfills> projections, IReadOnlyCollection<string?> tenants, CancellationToken token)
    {
        if (projections.Count == 0)
        {
            return [];
        }

        var completed = (await FetchAsync(database, token).ConfigureAwait(false))
            .Where(x => x.IsCompleted).Select(x => x.Name).ToHashSet();

        return ExpectedNames(projections, tenants).Where(x => !completed.Contains(x)).ToArray();
    }
}
