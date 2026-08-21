#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Testing.Harness;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace Marten.EntityFrameworkCore.Tests;

public record SearchCriteriaChanged(IReadOnlyList<Guid> CriteriaIds);

public class SearchProjection
{
    public Guid Id { get; set; }
    public int ChangeCount { get; set; }
}

public class SearchProjectionDbContext: DbContext
{
    public SearchProjectionDbContext(DbContextOptions<SearchProjectionDbContext> options) : base(options)
    {
    }

    public DbSet<SearchProjection> SearchProjections => Set<SearchProjection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SearchProjection>(entity =>
        {
            entity.ToTable("ef_search_projections");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.ChangeCount).HasColumnName("change_count");
        });
    }
}

public class SearchProjector:
    EfCoreMultiStreamProjection<SearchProjection, Guid, SearchProjectionDbContext>
{
    // How many slices were ever inside ApplyEventsAsync at the same instant. This is the observable
    // consequence of jasperfx#683: a storage that says it cannot take concurrent slices gets them
    // applied inline, so this can only ever reach 1. Before the fix the 10-wide block drove it higher.
    private static int s_current;
    public static int MaxObservedConcurrency;

    public static void ResetConcurrencyProbe()
    {
        Interlocked.Exchange(ref s_current, 0);
        Interlocked.Exchange(ref MaxObservedConcurrency, 0);
    }

    public SearchProjector()
    {
        CustomGrouping((_, events, grouping) =>
        {
            // A single source event deliberately fans out into more projection slices than the
            // AggregationRunner's Block<EventSliceExecution>(10, ...) worker count.
            grouping.AddEvents<SearchCriteriaChanged>(x => x.CriteriaIds, events);
            return Task.CompletedTask;
        });
    }

    protected override async ValueTask<(SearchProjection?, ActionType)> ApplyEventsAsync(
        SearchProjection? snapshot,
        Guid identity,
        IReadOnlyList<IEvent> events,
        IQuerySession session,
        SearchProjectionDbContext dbContext,
        CancellationToken token)
    {
        var running = Interlocked.Increment(ref s_current);
        try
        {
            // Record the high-water mark without a lock; a plain compare-and-swap loop is enough.
            int observed;
            while (running > (observed = Volatile.Read(ref MaxObservedConcurrency)))
            {
                Interlocked.CompareExchange(ref MaxObservedConcurrency, running, observed);
            }

            // Long enough that genuinely parallel slices overlap here rather than merely interleaving.
            await Task.Delay(15, token);

            snapshot ??= new SearchProjection { Id = identity };
            snapshot.ChangeCount += events.Count;
            return (snapshot, ActionType.Store);
        }
        finally
        {
            Interlocked.Decrement(ref s_current);
        }
    }
}

/// <summary>
/// #5266. <c>AggregationRunner</c> applies every slice in a range through a fixed 10-wide block, and
/// hands each one the <em>same</em> <see cref="IProjectionStorage{TDoc,TId}" />. The EF Core storage
/// wraps a single <see cref="DbContext" /> per tenant/batch, and a <c>DbContext</c> is not thread-safe:
/// a multi-stream projection with custom grouping fans one event out into many slices, so up to ten of
/// them concurrently call <c>Entry()</c> / <c>FindAsync</c> and mutate the same change tracker. That
/// corrupts EF Core's identity map, surfacing as an <see cref="InvalidOperationException" /> out of
/// <c>Dictionary.TryInsert</c> or a <see cref="NullReferenceException" /> out of
/// <c>ChangeDetector.DetectChanges</c>.
/// <para>
/// Fixed by jasperfx#683, which lets a storage declare that it cannot take concurrent slices; the runner
/// then applies them inline instead of posting them into the block.
/// </para>
/// </summary>
public class Bug_5266_shared_dbcontext_across_slices: IAsyncLifetime
{
    private DocumentStore _store = null!;
    private SearchProjector _projection = null!;

    public async ValueTask InitializeAsync()
    {
        _projection = new SearchProjector();
        _store = DocumentStore.For(options =>
        {
            options.Connection(ConnectionSource.ConnectionString);
            options.DatabaseSchemaName = "efcore_rebuild_concurrency";
            options.Add(_projection, ProjectionLifecycle.Async);
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
    }

    public ValueTask DisposeAsync()
    {
        _store?.Dispose();
        return default;
    }

    /// <summary>
    /// Counted against the EF Core table, not <c>session.Query&lt;SearchProjection&gt;()</c>. The
    /// projection writes through the DbContext into ef_search_projections; Marten has no document
    /// storage for this type at all, so querying the session would report zero however the rebuild went.
    /// </summary>
    private async Task<int> ProjectionCountAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select count(*) from efcore_rebuild_concurrency.ef_search_projections";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    /// <summary>
    /// The deterministic half. The reporter's repro is a genuine data race and so is flaky by nature —
    /// it usually throws, which is not something to gate CI on. This pins the contract that removes the
    /// race instead, and it cannot flake.
    /// </summary>
    [Fact]
    public void the_ef_core_storage_declares_that_it_cannot_take_concurrent_slices()
    {
        var storageType = typeof(EfCoreProjectionExtensions).Assembly
            .GetType("Marten.EntityFrameworkCore.EfCoreProjectionStorage`3")!
            .MakeGenericType(typeof(SearchProjection), typeof(Guid), typeof(SearchProjectionDbContext));

        var storage = (IProjectionStorage<SearchProjection, Guid>)Activator.CreateInstance(
            storageType,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.CreateInstance,
            null,
            [null!, "*DEFAULT*"],
            null)!;

        storage.IsThreadSafe.ShouldBeFalse();
    }

    /// <summary>
    /// The behavioural regression test, and it is deterministic. A storage that declares it cannot take
    /// concurrent slices must have them applied one at a time, so no two can ever be inside
    /// <c>ApplyEventsAsync</c> at once. Twenty slices from one event against a 10-wide block is exactly
    /// the reporter's shape; before jasperfx#683 this reached the block's worker count.
    /// </summary>
    [Fact]
    public async Task slices_are_never_applied_concurrently_against_the_shared_dbcontext()
    {
        SearchProjector.ResetConcurrencyProbe();

        var criteriaIds = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToArray();

        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream(Guid.NewGuid(), new SearchCriteriaChanged(criteriaIds));
            await session.SaveChangesAsync();
        }

        using (var daemon = await _store.BuildProjectionDaemonAsync())
        {
            await daemon.RebuildProjectionAsync<SearchProjector>(CancellationToken.None);
        }

        // Guard the guard: if nothing was applied the concurrency assertion below is vacuous.
        (await ProjectionCountAsync()).ShouldBe(criteriaIds.Length);

        SearchProjector.MaxObservedConcurrency.ShouldBe(1);
    }

    /// <summary>
    /// The reporter's repro, kept as scenario coverage rather than as a race detector: it is a genuine
    /// data race, so it only <em>usually</em> failed, and it passes on the unfixed build often enough to
    /// prove nothing on its own. The concurrency assertion above is what actually pins the fix.
    /// </summary>
    [Fact]
    public async Task rebuild_across_slices_creates_all_projections()
    {
        var criteriaIds = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToArray();

        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream(Guid.NewGuid(), new SearchCriteriaChanged(criteriaIds));
            await session.SaveChangesAsync();
        }

        using (var daemon = await _store.BuildProjectionDaemonAsync())
        {
            await daemon.RebuildProjectionAsync<SearchProjector>(CancellationToken.None);
        }

        (await ProjectionCountAsync()).ShouldBe(criteriaIds.Length);
    }
}
