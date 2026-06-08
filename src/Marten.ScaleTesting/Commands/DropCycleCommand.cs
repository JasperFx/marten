using System.Diagnostics;
using JasperFx;
using JasperFx.CommandLine;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Storage;
using Npgsql;
using Spectre.Console;
using Weasel.Postgresql;

namespace Marten.ScaleTesting.Commands;

/// <summary>
/// Local copy of <c>Marten.Events.Daemon.HighWater.HighWaterShardIdentity</c>'s public
/// constants. The Marten type is <c>internal</c>; rather than poking InternalsVisibleTo for a
/// dev-tool subcommand we mirror the two strings here. If either side drifts, the harness's
/// drop-cycle assertions stop matching the rows the cleaner actually leaves behind -- which
/// is the early-warning the user wants anyway.
/// </summary>
internal static class HighWaterMarkPerTenantPrefixCopy
{
    public const string StoreGlobal = "HighWaterMark";
    public const string PerTenantPrefix = StoreGlobal + ":";
}

/// <summary>
/// <c>dropcycle</c>: end-to-end smoke for the #4683 cleanup, against a dedicated
/// per-tenant-partitioned store so the leak the cleanup fixes is actually exercised
/// (the harness's main store config -- driven from Program.cs -- is Conjoined-only,
/// not UseTenantPartitionedEvents, so the per-tenant sequence + per-tenant progression
/// leaks don't even arise there).
///
/// Flow:
/// <list type="number">
///   <item>Build an isolated partitioned <see cref="Marten.DocumentStore"/> in its own schema</item>
///   <item>Register <c>--tenants</c> tenants and append a handful of events to each so the per-tenant
///     sequence + progression rows actually populate</item>
///   <item>Seed extra per-tenant progression rows directly (covers projection rows the test
///     run doesn't naturally produce -- the cleanup must drop these too)</item>
///   <item>Snapshot the target tenant's footprint</item>
///   <item>Call <c>DeleteAllTenantDataAsync</c></item>
///   <item>Assert every per-tenant artifact is gone and the peer tenants + store-global
///     <c>HighWaterMark</c> survive</item>
///   <item>Unless <c>--skip-readd</c>, re-add the tenant + append a small batch and verify
///     the new partition + sequence + progression are clean</item>
/// </list>
///
/// Idempotent enough to re-run: <c>--wipe</c> drops + recreates the dedicated schema first.
/// </summary>
[Description("Exercise #4683 drop-tenant cleanup against an isolated partitioned store: register tenants, seed, drop one, verify the per-tenant sequence + progression rows are gone (peers + store-global preserved); optionally re-add + verify fresh.")]
public sealed class DropCycleCommand: JasperFxAsyncCommand<DropCycleInput>
{
    private const string Schema = "scaletest_dropcycle";

    public override async Task<bool> Execute(DropCycleInput input)
    {
        var tenantId = input.TenantId();
        var peerTenantId = input.TenantIndexFlag == 0 ? PeerOf(input, 1) : PeerOf(input, 0);
        var totalElapsed = Stopwatch.StartNew();
        var checks = new List<(string Phase, string Check, bool Passed, string Detail)>();

        AnsiConsole.MarkupLine($"[blue]dropcycle: schema=[yellow]{Schema}[/] target=[yellow]{tenantId}[/] peer=[yellow]{peerTenantId}[/][/]");

        // ---- Setup an isolated partitioned store ------------------------------

        await EnsureFreshSchemaAsync().ConfigureAwait(false);

        using var store = Marten.DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = Schema;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Events.AddEventType<DropCycleEvent>();
        });

        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync().ConfigureAwait(false);

        // Register every tenant + append a few events to advance each per-tenant sequence
        // and create the partition tables.
        var allTenants = Enumerable.Range(0, input.TenantsFlag).Select(i => PeerOf(input, i)).ToArray();
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, allTenants).ConfigureAwait(false);
        foreach (var t in allTenants)
        {
            await using var session = store.LightweightSession(t);
            session.Events.StartStream(Guid.NewGuid(),
                new DropCycleEvent($"seed-{t}-1"),
                new DropCycleEvent($"seed-{t}-2"));
            await session.SaveChangesAsync().ConfigureAwait(false);
        }

        // Seed extra projection-shape progression rows for the target + a peer, so the test
        // covers ShardName-grammar rows (the canonical per-projection-per-tenant catch-up
        // rows) and HighWaterMark-grammar rows the per-tenant tracker produces.
        await SeedProgressionRowsAsync(new[]
        {
            // store-global -- must survive
            HighWaterMarkPerTenantPrefixCopy.StoreGlobal,
            "SomeProjection:All",
            // target tenant -- must be deleted
            $"DropCycleProjection:All:{tenantId}",
            $"VersionedProjection:V2:All:{tenantId}",
            $"{HighWaterMarkPerTenantPrefixCopy.PerTenantPrefix}{tenantId}",
            // peer -- must survive
            $"DropCycleProjection:All:{peerTenantId}",
            $"{HighWaterMarkPerTenantPrefixCopy.PerTenantPrefix}{peerTenantId}",
        }).ConfigureAwait(false);

        // ---- Pre-drop snapshot ------------------------------------------------

        var pre = await SnapshotAsync(tenantId).ConfigureAwait(false);
        var peerPre = await SnapshotAsync(peerTenantId).ConfigureAwait(false);
        AnsiConsole.MarkupLine(
            $"[grey]pre-drop target: partition={pre.PartitionExists} seq={pre.SequenceExists}" +
            $" per-tenant-rows={pre.ProgressionRows.Count}; peer per-tenant-rows={peerPre.ProgressionRows.Count}[/]");

        if (!pre.PartitionExists || !pre.SequenceExists || pre.ProgressionRows.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]Pre-drop snapshot didn't see the expected target footprint. Setup is broken; aborting.[/]");
            return false;
        }

        // ---- Drop -------------------------------------------------------------

        var dropSw = Stopwatch.StartNew();
        try
        {
            await store.Advanced.DeleteAllTenantDataAsync(tenantId, CancellationToken.None).ConfigureAwait(false);
            dropSw.Stop();
            AnsiConsole.MarkupLine($"[green]DeleteAllTenantDataAsync completed in {dropSw.Elapsed.TotalSeconds:N2}s[/]");
        }
        catch (Exception e)
        {
            dropSw.Stop();
            AnsiConsole.MarkupLine($"[red]DeleteAllTenantDataAsync FAILED after {dropSw.Elapsed.TotalSeconds:N2}s[/]");
            AnsiConsole.WriteException(e);
            return false;
        }

        // ---- Post-drop assertions ---------------------------------------------

        var post = await SnapshotAsync(tenantId).ConfigureAwait(false);
        var peerPost = await SnapshotAsync(peerTenantId).ConfigureAwait(false);
        var globalPresent = await ProgressionRowExistsAsync(HighWaterMarkPerTenantPrefixCopy.StoreGlobal).ConfigureAwait(false);
        var sharedProjectionPresent = await ProgressionRowExistsAsync("SomeProjection:All").ConfigureAwait(false);

        checks.Add(("drop", "partition removed", !post.PartitionExists,
            post.PartitionExists ? "mt_events_<tenant> partition still present" : "ok"));
        checks.Add(("drop", "sequence removed", !post.SequenceExists,
            post.SequenceExists ? "mt_events_sequence_<tenant> still present" : "ok"));
        checks.Add(("drop", "per-tenant progression rows removed", post.ProgressionRows.Count == 0,
            post.ProgressionRows.Count == 0 ? "ok" : string.Join(", ", post.ProgressionRows)));

        checks.Add(("drop", "peer partition untouched", peerPost.PartitionExists,
            peerPost.PartitionExists ? "ok" : "peer partition disappeared"));
        checks.Add(("drop", "peer sequence untouched", peerPost.SequenceExists,
            peerPost.SequenceExists ? "ok" : "peer sequence disappeared"));
        checks.Add(("drop", "peer progression rows untouched",
            peerPost.ProgressionRows.Count == peerPre.ProgressionRows.Count,
            $"pre={peerPre.ProgressionRows.Count} post={peerPost.ProgressionRows.Count}"));

        checks.Add(("drop", "store-global HighWaterMark preserved", globalPresent,
            globalPresent ? "ok" : "MISSING -- per-tenant cleanup wrongly removed the store-global row"));
        checks.Add(("drop", "store-global SomeProjection:All preserved", sharedProjectionPresent,
            sharedProjectionPresent ? "ok" : "MISSING -- per-tenant cleanup wrongly removed a store-global projection row"));

        // ---- Re-add + verify --------------------------------------------------

        if (!input.SkipReaddFlag)
        {
            try
            {
                await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenantId).ConfigureAwait(false);

                var readded = await SnapshotAsync(tenantId).ConfigureAwait(false);
                checks.Add(("re-add", "partition re-created", readded.PartitionExists,
                    readded.PartitionExists ? "ok" : "AddMartenManagedTenantsAsync did not create the partition"));
                checks.Add(("re-add", "sequence re-created", readded.SequenceExists,
                    readded.SequenceExists ? "ok" : "AddMartenManagedTenantsAsync did not create the sequence"));
                checks.Add(("re-add", "no leftover per-tenant progression rows", readded.ProgressionRows.Count == 0,
                    readded.ProgressionRows.Count == 0 ? "ok" : string.Join(", ", readded.ProgressionRows)));

                // A non-zero last_value here would mean the cleanup didn't actually drop the
                // old sequence and we inherited its high-water value -- the precise leak the
                // orphan-pin originally documented.
                var seqValue = await ReadSequenceLastValueAsync(tenantId).ConfigureAwait(false);
                checks.Add(("re-add", "sequence starts fresh (last_value = 0 pre-append)", seqValue == 0,
                    seqValue == 0 ? "ok" : $"sequence carries last_value={seqValue} from before the drop"));

                if (input.ReaddEventCountFlag > 0)
                {
                    await using var session = store.LightweightSession(tenantId);
                    var streamId = Guid.NewGuid();
                    var events = Enumerable.Range(0, input.ReaddEventCountFlag)
                        .Select(i => (object)new DropCycleEvent($"readd-{i}")).ToArray();
                    session.Events.StartStream(streamId, events);
                    await session.SaveChangesAsync().ConfigureAwait(false);

                    var streams = await CountTenantStreamsAsync(tenantId).ConfigureAwait(false);
                    checks.Add(("re-add", $"append of {input.ReaddEventCountFlag} events lands in new partition",
                        streams >= 1, streams >= 1 ? $"streams={streams}" : "no streams visible after append"));

                    var seqAfter = await ReadSequenceLastValueAsync(tenantId).ConfigureAwait(false);
                    checks.Add(("re-add", "sequence advanced after the append",
                        seqAfter > 0, seqAfter > 0 ? $"last_value={seqAfter}" : "sequence stayed at 0 despite events"));
                }
            }
            catch (Exception e)
            {
                AnsiConsole.MarkupLine("[red]Re-add phase FAILED[/]");
                AnsiConsole.WriteException(e);
                checks.Add(("re-add", "no exception thrown during re-add + seed", false, e.GetType().Name + ": " + e.Message));
            }
        }

        // ---- Summary ----------------------------------------------------------

        totalElapsed.Stop();
        PrintSummary(checks, totalElapsed.Elapsed, pre, post);
        return checks.All(c => c.Passed);
    }

    // ---- Helpers --------------------------------------------------------------

    private static string PeerOf(DropCycleInput input, int index)
        => new Seeding.SeedOptions(TenantCount: input.TenantsFlag).TenantId(index);

    private static async Task EnsureFreshSchemaAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var drop = new NpgsqlCommand($"drop schema if exists \"{Schema}\" cascade", conn);
        await drop.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task SeedProgressionRowsAsync(IEnumerable<string> names)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        foreach (var name in names)
        {
            await using var cmd = new NpgsqlCommand(
                $"insert into \"{Schema}\".\"mt_event_progression\" (name, last_seq_id) values (@n, 0) on conflict (name) do nothing",
                conn);
            cmd.Parameters.AddWithValue("n", name);
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    private sealed record TenantSnapshot(bool PartitionExists, bool SequenceExists, List<string> ProgressionRows);

    private static async Task<TenantSnapshot> SnapshotAsync(string tenantId)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);

        await using var partitionCmd = new NpgsqlCommand(
            "select count(*) from information_schema.tables where table_schema = @s and table_name = @t", conn);
        partitionCmd.Parameters.AddWithValue("s", Schema);
        partitionCmd.Parameters.AddWithValue("t", $"mt_events_{tenantId}");
        var partitionExists = (long)(await partitionCmd.ExecuteScalarAsync().ConfigureAwait(false))! == 1L;

        await using var seqCmd = new NpgsqlCommand(
            "select count(*) from pg_sequences where schemaname = @s and sequencename = @n", conn);
        seqCmd.Parameters.AddWithValue("s", Schema);
        seqCmd.Parameters.AddWithValue("n", $"mt_events_sequence_{tenantId}");
        var sequenceExists = (long)(await seqCmd.ExecuteScalarAsync().ConfigureAwait(false))! == 1L;

        var rows = new List<string>();
        await using var progCmd = new NpgsqlCommand(
            $"select name from \"{Schema}\".\"mt_event_progression\"", conn);
        await using var reader = await progCmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var name = reader.GetString(0);
            if (MentionsTenant(name, tenantId)) rows.Add(name);
        }

        return new TenantSnapshot(partitionExists, sequenceExists, rows);
    }

    private static bool MentionsTenant(string name, string tenantId)
    {
        if (name.StartsWith(HighWaterMarkPerTenantPrefixCopy.PerTenantPrefix, StringComparison.Ordinal))
        {
            return name.Substring(HighWaterMarkPerTenantPrefixCopy.PerTenantPrefix.Length) == tenantId;
        }
        return ShardName.TryParse(name, out var shard) && shard?.TenantId == tenantId;
    }

    private static async Task<bool> ProgressionRowExistsAsync(string name)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            $"select count(*) from \"{Schema}\".\"mt_event_progression\" where name = @n", conn);
        cmd.Parameters.AddWithValue("n", name);
        return (long)(await cmd.ExecuteScalarAsync().ConfigureAwait(false))! == 1L;
    }

    private static async Task<long> ReadSequenceLastValueAsync(string tenantId)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            "select last_value from pg_sequences where schemaname = @s and sequencename = @n", conn);
        cmd.Parameters.AddWithValue("s", Schema);
        cmd.Parameters.AddWithValue("n", $"mt_events_sequence_{tenantId}");
        var raw = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
        return raw is long v ? v : (raw is null || raw == DBNull.Value ? 0L : Convert.ToInt64(raw));
    }

    private static async Task<long> CountTenantStreamsAsync(string tenantId)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand(
            $"select count(*) from \"{Schema}\".\"mt_streams\" where tenant_id = @t", conn);
        cmd.Parameters.AddWithValue("t", tenantId);
        return (long)(await cmd.ExecuteScalarAsync().ConfigureAwait(false))!;
    }

    private static void PrintSummary(
        List<(string Phase, string Check, bool Passed, string Detail)> checks,
        TimeSpan elapsed,
        TenantSnapshot pre,
        TenantSnapshot post)
    {
        AnsiConsole.WriteLine();
        var table = new Table()
            .AddColumn("Phase")
            .AddColumn("Check")
            .AddColumn("Pass")
            .AddColumn("Detail");
        foreach (var c in checks)
        {
            var passColor = c.Passed ? "green" : "red";
            var passText = c.Passed ? "OK" : "FAIL";
            table.AddRow(c.Phase, c.Check, $"[{passColor}]{passText}[/]", c.Detail);
        }
        AnsiConsole.Write(table);

        var passing = checks.Count(c => c.Passed);
        var total = checks.Count;
        var color = passing == total ? "green" : "red";
        AnsiConsole.MarkupLine($"[{color}]{passing}/{total} checks passed[/] · total {elapsed.TotalSeconds:N1}s");
        AnsiConsole.MarkupLine(
            $"[grey]pre-drop target: partition={pre.PartitionExists} sequence={pre.SequenceExists}" +
            $" per-tenant-rows={pre.ProgressionRows.Count}; post-drop per-tenant-rows={post.ProgressionRows.Count}[/]");
    }
}

/// <summary>Tiny event for the drop-cycle isolated store. Standalone -- not part of the
/// Telehealth domain used elsewhere in the harness -- so this test is fully self-contained.</summary>
public record DropCycleEvent(string Marker);
