#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.CommandLine;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

#region test events and aggregates

public record CliFreighterLaunched(string Name);

public record CliFreighterDocked(string Port);

public partial class CliQueryFreighter
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Dockings { get; set; }

    public static CliQueryFreighter Create(CliFreighterLaunched e) => new() { Name = e.Name };

    public void Apply(CliFreighterDocked _) => Dockings++;
}

/// <summary>A second aggregate type over the same events, so the type filter has a decoy.</summary>
public partial class CliQueryTugboat
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Dockings { get; set; }

    public static CliQueryTugboat Create(CliFreighterLaunched e) => new() { Name = e.Name };

    public void Apply(CliFreighterDocked _) => Dockings++;
}

#endregion

/// <summary>
/// End-to-end coverage for the <c>stream-query</c> CLI command (jasperfx#740 / marten#5333)
/// against a real Marten store — the pattern the <c>event-query</c> wave established: drive
/// <see cref="StreamQueryCommand.Execute"/> with a real <see cref="StreamQueryInput"/> whose
/// <c>HostBuilder</c> wires Marten at the test database, capture stdout, parse the default JSON
/// report, and assert exact expected streams — including the <c>versionsSinceCompaction</c>
/// growth measure the Stream Compaction Policies threshold on.
/// </summary>
[Collection("CliCommands")]
public class stream_query_command_end_to_end
{
    private static IHostBuilder martenHostBuilder(string schemaName)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddMarten(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.DatabaseSchemaName = schemaName;
                opts.AutoCreateSchemaObjects = AutoCreate.All;
                opts.DisableNpgsqlLogging = true;

                opts.Events.AddEventType<CliFreighterLaunched>();
                opts.Events.AddEventType<CliFreighterDocked>();

                // Inline snapshots register both aggregate types store-side: what lets
                // CompactStreamAsync find an aggregator, and what StartStream<T> stamps the
                // stream's aggregate-type identity from.
                opts.Projections.Snapshot<CliQueryFreighter>(SnapshotLifecycle.Inline);
                opts.Projections.Snapshot<CliQueryTugboat>(SnapshotLifecycle.Inline);
            }));
    }

    private static async Task<(bool Success, JsonDocument Report)> executeAsync(StreamQueryInput input)
    {
        var original = Console.Out;
        var console = new StringWriter();
        Console.SetOut(console);

        bool success;
        try
        {
            success = await new StreamQueryCommand().Execute(input);
        }
        finally
        {
            Console.SetOut(original);
        }

        return (success, CommandJsonOutput.ExtractReport(console.ToString()));
    }

    [Fact]
    public async Task queries_stream_states_with_the_compaction_policy_filters()
    {
        const string schema = "stream_query_cli_e2e";

        // Seed through an ordinary Marten host: two freighters — one compacted through version 3
        // (growth 2), one never compacted (growth 5) — and a tugboat decoy that fails only the
        // aggregate-type filter.
        Guid compacted;
        Guid overgrown;
        using (var host = martenHostBuilder(schema).Build())
        {
            var store = host.Services.GetRequiredService<IDocumentStore>();
            await store.Advanced.Clean.CompletelyRemoveAllAsync();

            await using var session = store.LightweightSession();

            // One save per stream with real wall-clock gaps: mt_streams.created defaults to the
            // transaction timestamp, so same-save streams tie on Created and fall back to the Id
            // tiebreak — real creation order needs distinct transactions.
            compacted = Guid.NewGuid();
            session.Events.StartStream<CliQueryFreighter>(compacted, new CliFreighterLaunched("Cargolux"),
                new CliFreighterDocked("Kiel"), new CliFreighterDocked("Aarhus"),
                new CliFreighterDocked("Gdansk"), new CliFreighterDocked("Riga"));
            await session.SaveChangesAsync();
            await Task.Delay(30);

            overgrown = Guid.NewGuid();
            session.Events.StartStream<CliQueryFreighter>(overgrown, new CliFreighterLaunched("Evergreen"),
                new CliFreighterDocked("Kiel"), new CliFreighterDocked("Aarhus"),
                new CliFreighterDocked("Gdansk"), new CliFreighterDocked("Riga"));
            await session.SaveChangesAsync();
            await Task.Delay(30);

            session.Events.StartStream<CliQueryTugboat>(Guid.NewGuid(), new CliFreighterLaunched("Pushy"),
                new CliFreighterDocked("Kiel"), new CliFreighterDocked("Aarhus"),
                new CliFreighterDocked("Gdansk"), new CliFreighterDocked("Riga"));
            await session.SaveChangesAsync();

            await session.Events.CompactStreamAsync<CliQueryFreighter>(compacted, x => x.Version = 3);
            await session.SaveChangesAsync();
        }

        // Run 1: the type filter alone — both freighters, creation order, exact watermarks.
        var (success, report) = await executeAsync(new StreamQueryInput
        {
            HostBuilder = martenHostBuilder(schema),
            AggregateTypeFlag = nameof(CliQueryFreighter),
            PageSizeFlag = 10
        });

        success.ShouldBeTrue();

        var root = report.RootElement;
        root.TryGetProperty("error", out _).ShouldBeFalse();
        root.GetProperty("totalCount").GetInt32().ShouldBe(2);

        var streams = root.GetProperty("streams").EnumerateArray().ToList();
        streams.Count.ShouldBe(2);

        // Creation order, oldest first — the command's stated ordering.
        streams.Select(x => x.GetProperty("streamId").GetString())
            .ShouldBe([compacted.ToString(), overgrown.ToString()]);

        var compactedRow = streams[0];
        compactedRow.GetProperty("version").GetInt64().ShouldBe(5);
        compactedRow.GetProperty("compactedVersion").GetInt64().ShouldBe(3);
        compactedRow.GetProperty("versionsSinceCompaction").GetInt64().ShouldBe(2);
        compactedRow.GetProperty("aggregateType").GetString().ShouldBe(typeof(CliQueryFreighter).FullName);

        var overgrownRow = streams[1];
        overgrownRow.GetProperty("version").GetInt64().ShouldBe(5);
        overgrownRow.GetProperty("compactedVersion").GetInt64().ShouldBe(0);
        overgrownRow.GetProperty("versionsSinceCompaction").GetInt64().ShouldBe(5);

        // Run 2: the full compaction-policy shape — type AND growth threshold. The compacted
        // freighter (raw version 5!) is the load-bearing decoy for thresholding on Version
        // instead of growth; the tugboat for a dropped type filter.
        var (policySuccess, policyReport) = await executeAsync(new StreamQueryInput
        {
            HostBuilder = martenHostBuilder(schema),
            AggregateTypeFlag = nameof(CliQueryFreighter),
            VersionAboveCompactedFlag = 3,
            PageSizeFlag = 10
        });

        policySuccess.ShouldBeTrue();

        var policyRoot = policyReport.RootElement;
        policyRoot.GetProperty("totalCount").GetInt32().ShouldBe(1);
        var match = policyRoot.GetProperty("streams").EnumerateArray().Single();
        match.GetProperty("streamId").GetString().ShouldBe(overgrown.ToString());
        match.GetProperty("versionsSinceCompaction").GetInt64().ShouldBe(5);
    }

    /// <summary>
    /// The honesty path, end to end: a tenant scope against a store with no tenant dimension is
    /// refused by name with a failing return — never answered with unscoped rows that read as
    /// tenant-scoped — and the same JSON report shape carries the error.
    /// </summary>
    [Fact]
    public async Task refuses_a_tenant_scope_on_a_tenantless_store()
    {
        const string schema = "stream_query_cli_refuse";

        using (var host = martenHostBuilder(schema).Build())
        {
            var store = host.Services.GetRequiredService<IDocumentStore>();
            await store.Advanced.Clean.CompletelyRemoveAllAsync();

            await using var session = store.LightweightSession();
            session.Events.StartStream<CliQueryFreighter>(Guid.NewGuid(), new CliFreighterLaunched("Solo"));
            await session.SaveChangesAsync();
        }

        var (success, report) = await executeAsync(new StreamQueryInput
        {
            HostBuilder = martenHostBuilder(schema),
            TenantFlag = "tenant-a"
        });

        success.ShouldBeFalse();

        var root = report.RootElement;
        root.GetProperty("error").GetString().ShouldNotBeNull();
        root.GetProperty("error").GetString()!.ShouldContain("tenant-a");
        root.GetProperty("totalCount").GetInt32().ShouldBe(0);
        root.GetProperty("streams").GetArrayLength().ShouldBe(0);
    }
}
