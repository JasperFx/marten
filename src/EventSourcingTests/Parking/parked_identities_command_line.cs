using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Spectre.Console;
using Xunit;

namespace EventSourcingTests.Parking;

/// <summary>
///     marten#6122: <c>projections parked</c> end to end against Marten. Each command runs in a host of its own, the
///     way an operator runs it against a deployed application, and never hosts the daemon: a replay or a discard is a
///     durable request that the node running the shard (the daemon each test builds, or none) picks up.
/// </summary>
public class parked_identities_command_line: OneOffConfigurationsContext
{
    private static readonly TimeSpan Generous = 60.Seconds();

    private readonly List<Guid> _poisoned = new();

    // marten#6132: also fold the ledgers into NaturalKeyLedger, whose aggregate has a natural key
    private bool _withNaturalKeys;

    public parked_identities_command_line()
    {
        StoreOptions(configure);
    }

    private void configure(StoreOptions opts)
    {
        opts.Projections.Errors.ApplyErrors = ProjectionErrorAction.Park;
        opts.Projections.Snapshot<GuidLedger>(SnapshotLifecycle.Async);

        if (_withNaturalKeys)
        {
            opts.Projections.Snapshot<NaturalKeyLedger>(SnapshotLifecycle.Async);
        }
    }

    public override async ValueTask DisposeAsync()
    {
        foreach (var stream in _poisoned) LedgerPoison.Off(StorageConstants.DefaultTenantId, stream);
        await base.DisposeAsync();
    }

    private void poison(Guid stream)
    {
        LedgerPoison.On(StorageConstants.DefaultTenantId, stream);
        _poisoned.Add(stream);
    }

    private static void unpoison(Guid stream) => LedgerPoison.Off(StorageConstants.DefaultTenantId, stream);

    /// <summary>Run one command in a host of its own and capture what it wrote.</summary>
    private async Task<(int ExitCode, string Output)> runAsync(params string[] args)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer)
        });

        // Wide enough that a table never wraps an identity
        console.Profile.Width = 400;

        var original = AnsiConsole.Console;
        AnsiConsole.Console = console;
        try
        {
            var exitCode = await Host.CreateDefaultBuilder()
                .ConfigureServices(services => services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = SchemaName;
                    configure(opts);
                }))
                .RunJasperFxCommands(args);

            return (exitCode, writer.ToString());
        }
        finally
        {
            AnsiConsole.Console = original;
        }
    }

    private async Task appendAsync(Guid stream, params object[] events)
    {
        await using var session = theStore.LightweightSession();
        session.Events.Append(stream, events);
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<IReadOnlyList<ParkedIdentity>> parkedAsync()
        => await ((IEventDatabase)theStore.Storage.Database).FetchParkedIdentitiesAsync(
            new ParkedIdentityQuery { ProjectionName = nameof(GuidLedger) }, TestContext.Current.CancellationToken);

    private async Task<int?> postingsAsync(Guid stream)
    {
        await using var query = theStore.QuerySession();
        return (await query.LoadAsync<GuidLedger>(stream, TestContext.Current.CancellationToken))?.Postings;
    }

    /// <summary>
    ///     A ledger with one good posting, then a broken event and a posting held behind it. Returns the running daemon.
    /// </summary>
    private async Task<(Guid Stream, IProjectionDaemon Daemon)> aParkedLedgerAsync()
    {
        var stream = Guid.NewGuid();
        var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        await appendAsync(stream, new LedgerOpened("A-1"), new LedgerPosted());
        await theStore.WaitForNonStaleProjectionDataAsync(Generous);

        poison(stream);
        await appendAsync(stream, new LedgerBroken(), new LedgerPosted());
        await theStore.WaitForNonStaleProjectionDataAsync(Generous);

        (await parkedAsync()).ShouldHaveSingleItem().IdentityText.ShouldBe(stream.ToString("D"));
        (await postingsAsync(stream)).ShouldBe(1);

        return (stream, daemon);
    }

    [Fact]
    public async Task listing_shows_each_parked_identity_and_its_error()
    {
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var (exitCode, output) = await runAsync("projections", "parked");
        exitCode.ShouldBe(0);
        output.ShouldContain("Nothing is parked.");

        var (stream, daemon) = await aParkedLedgerAsync();
        await daemon.StopAllAsync();
        daemon.Dispose();

        (exitCode, output) = await runAsync("projections", "parked");
        exitCode.ShouldBe(0);
        output.ShouldContain(stream.ToString("D"));
        output.ShouldContain(nameof(GuidLedger));
        output.ShouldContain($"Ledger {stream} in {StorageConstants.DefaultTenantId} is poisoned");

        (exitCode, output) = await runAsync("projections", "parked", "--projection", nameof(GuidLedger));
        exitCode.ShouldBe(0);
        output.ShouldContain(stream.ToString("D"));
    }

    [Fact]
    public async Task listing_shows_the_natural_key_when_the_aggregate_has_one()
    {
        _withNaturalKeys = true;
        StoreOptions(configure);

        // The same ledger parks in GuidLedger, which has no natural key, and in NaturalKeyLedger, which has one
        var (stream, daemon) = await aParkedLedgerAsync();
        await daemon.StopAllAsync();
        daemon.Dispose();

        var (exitCode, output) = await runAsync("projections", "parked");
        exitCode.ShouldBe(0, output);
        output.ShouldContain("Natural Key");

        var lines = output.Split('\n').Where(x => x.Contains(stream.ToString("D"))).ToList();
        lines.Count.ShouldBe(2, output);

        // NaturalKeyLedger's row shows the key the ledger was opened with, GuidLedger's row leaves it blank.
        // Case-sensitive on purpose: both rows carry the stream id, and a lower-case Guid such as
        // "34e22cda-1ed4-..." contains "a-1", which Shouldly's default case-insensitive comparison matched
        lines.Single(x => x.Contains(nameof(NaturalKeyLedger))).ShouldContain("A-1", Case.Sensitive);
        lines.Single(x => x.Contains($" {nameof(GuidLedger)} ")).ShouldNotContain("A-1", Case.Sensitive);
    }

    [Fact]
    public async Task a_replay_waits_for_the_node_running_the_shard()
    {
        var (stream, daemon) = await aParkedLedgerAsync();
        using var _ = daemon;

        try
        {
            // The fix is deployed
            unpoison(stream);

            var (exitCode, output) = await runAsync("projections", "parked", "--projection", nameof(GuidLedger),
                "--replay", "--identity", stream.ToString("D"), "--wait", "60");

            exitCode.ShouldBe(0, output);
            output.ShouldContain("asked for a replay of 1 parked identity of GuidLedger");
            output.ShouldContain("Done");

            (await parkedAsync()).ShouldBeEmpty();
            await theStore.WaitForNonStaleProjectionDataAsync(Generous);
            (await postingsAsync(stream)).ShouldBe(3);
        }
        finally
        {
            await daemon.StopAllAsync();
        }
    }

    [Fact]
    public async Task a_discard_is_held_until_the_shard_next_starts()
    {
        var (stream, daemon) = await aParkedLedgerAsync();
        await daemon.StopAllAsync();
        daemon.Dispose();

        var (exitCode, output) = await runAsync("projections", "parked", "--projection", nameof(GuidLedger),
            "--discard", "--identity", stream.ToString("D"), "--yes");

        exitCode.ShouldBe(0, output);
        output.ShouldContain("asked for a discard of 1 parked identity of GuidLedger");
        output.ShouldContain("No node is running GuidLedger");

        (await parkedAsync()).ShouldHaveSingleItem().RequestedAction.ShouldBe(ParkedAction.Discard);

        // The event is still broken. Discarding dead-letters it and applies the rest
        using var restarted = await theStore.BuildProjectionDaemonAsync();
        await restarted.StartAllAsync();

        // jasperfx#1263: a restarted shard only loads its parked set in a batch, so it needs an event to see the
        // request. Drop this append once that's fixed.
        await appendAsync(Guid.NewGuid(), new LedgerOpened("B-1"));

        try
        {
            var deadline = DateTimeOffset.UtcNow.Add(Generous);
            while ((await parkedAsync()).Any())
            {
                DateTimeOffset.UtcNow.ShouldBeLessThan(deadline, "The restarted shard never took the discard");
                await Task.Delay(250.Milliseconds(), TestContext.Current.CancellationToken);
            }

            await theStore.WaitForNonStaleProjectionDataAsync(Generous);
            (await postingsAsync(stream)).ShouldBe(2);

            await using var query = theStore.QuerySession();
            (await query.Query<DeadLetterEvent>().CountAsync(x => x.ProjectionName == nameof(GuidLedger),
                TestContext.Current.CancellationToken)).ShouldBe(1);
        }
        finally
        {
            await restarted.StopAllAsync();
        }
    }

    [Fact]
    public async Task a_discard_is_refused_without_an_identity()
    {
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var (exitCode, output) = await runAsync("projections", "parked", "--projection", nameof(GuidLedger),
            "--discard", "--yes");

        exitCode.ShouldNotBe(0);
        output.ShouldContain("A discard is one identity at a time.");
    }
}
