#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events.CommandLine;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

#region test events

public record CliCargoLoaded(string Cargo);

public record CliCargoInspected(string Inspector);

#endregion

/// <summary>
/// End-to-end coverage for the <c>event-query</c> CLI command (jasperfx#737 / JasperFx 2.62.0)
/// against a real Marten store — upstream carries only input-mapping unit tests, and nothing else
/// executes the command against a store. Drives <see cref="EventQueryCommand.Execute"/> directly
/// with a real <see cref="EventQueryInput"/> whose <c>HostBuilder</c> wires Marten at the test
/// database, exactly the way JasperFx.CommandLine hands the command an application host; stdout
/// is captured and the default JSON report parsed, so the assertion covers the full path an
/// operator or agent consumes: flags → EventQuery → QueryEventsAsync → JSON on stdout.
/// </summary>
public class event_query_command_end_to_end
{
    private static IHostBuilder martenHostBuilder(string schemaName, bool enableUserName = false)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddMarten(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.DatabaseSchemaName = schemaName;
                opts.AutoCreateSchemaObjects = AutoCreate.All;
                opts.DisableNpgsqlLogging = true;

                opts.Events.AddEventType<CliCargoLoaded>();
                opts.Events.AddEventType<CliCargoInspected>();

                if (enableUserName)
                {
                    opts.Events.MetadataConfig.UserNameEnabled = true;
                }
            }));
    }

    /// <summary>
    /// Runs the command against a fresh host the same way the CLI does — the command builds and
    /// disposes the host itself — capturing everything it writes to stdout.
    /// </summary>
    private static async Task<(bool Success, JsonDocument Report)> executeAsync(EventQueryInput input)
    {
        var original = Console.Out;
        var console = new StringWriter();
        Console.SetOut(console);

        bool success;
        try
        {
            success = await new EventQueryCommand().Execute(input);
        }
        finally
        {
            Console.SetOut(original);
        }

        var output = console.ToString();

        // The report is the only JSON object on stdout; anchor on its first brace so an incidental
        // plain-text line from the host bootstrap cannot break the parse.
        var start = output.IndexOf('{');
        start.ShouldBeGreaterThanOrEqualTo(0, $"expected a JSON report on stdout, but got: {output}");

        return (success, JsonDocument.Parse(output[start..]));
    }

    [Fact]
    public async Task queries_a_real_store_with_an_event_type_filter_and_page_size()
    {
        const string schema = "event_query_cli_e2e";

        // Seed through an ordinary Marten host: two Loaded events on separate streams with an
        // Inspected decoy between them, so the type filter has something to demonstrably exclude
        // and the ascending ordering shows across streams.
        string loadedTypeName;
        using (var host = martenHostBuilder(schema).Build())
        {
            var store = host.Services.GetRequiredService<IDocumentStore>();
            await store.Advanced.Clean.CompletelyRemoveAllAsync();

            loadedTypeName = ((DocumentStore)store).Options.EventGraph
                .EventMappingFor(typeof(CliCargoLoaded)).EventTypeName;

            await using var session = store.LightweightSession();
            session.Events.Append(Guid.NewGuid(), new CliCargoLoaded("grain"));
            session.Events.Append(Guid.NewGuid(), new CliCargoInspected("alice"));
            session.Events.Append(Guid.NewGuid(), new CliCargoLoaded("coal"));
            await session.SaveChangesAsync();
        }

        var (success, report) = await executeAsync(new EventQueryInput
        {
            HostBuilder = martenHostBuilder(schema),
            EventTypeFlag = loadedTypeName,
            PageSizeFlag = 10
        });

        success.ShouldBeTrue();

        var root = report.RootElement;

        // The report serializes with WhenWritingNull, so a successful run has no error member at all.
        root.TryGetProperty("error", out _).ShouldBeFalse();
        root.GetProperty("totalCount").GetInt32().ShouldBe(2);
        root.GetProperty("pageNumber").GetInt32().ShouldBe(1);
        root.GetProperty("pageSize").GetInt32().ShouldBe(10);
        root.GetProperty("hasMore").GetBoolean().ShouldBeFalse();
        // Uri.ToString() adds the trailing slash to the marten://main subject.
        root.GetProperty("store").GetString().ShouldBe("marten://main/");

        var events = root.GetProperty("events").EnumerateArray().ToList();
        events.Count.ShouldBe(2);

        // Exactly the two Loaded events, payloads included, in sequence-ascending order — the
        // Inspected decoy filtered out.
        events.ShouldAllBe(e => e.GetProperty("eventType").GetString() == loadedTypeName);
        events.Select(e => e.GetProperty("data").GetProperty("Cargo").GetString())
            .ShouldBe(["grain", "coal"]);
        events[0].GetProperty("sequence").GetInt64()
            .ShouldBeLessThan(events[1].GetProperty("sequence").GetInt64());
    }

    /// <summary>
    /// The honesty path, end to end: a filter on a metadata column this store does not capture is
    /// refused by name with a failing return — never an unfiltered answer that reads as filtered.
    /// The same JSON report shape carries the error, so a script parses one shape either way.
    /// </summary>
    [Fact]
    public async Task refuses_an_unsupported_filter_by_name_with_a_failing_return()
    {
        const string schema = "event_query_cli_refuse";

        using (var host = martenHostBuilder(schema).Build())
        {
            var store = host.Services.GetRequiredService<IDocumentStore>();
            await store.Advanced.Clean.CompletelyRemoveAllAsync();

            await using var session = store.LightweightSession();
            session.Events.Append(Guid.NewGuid(), new CliCargoLoaded("grain"));
            await session.SaveChangesAsync();
        }

        // user_name capture is NOT enabled on this host, so the jasperfx#737 guard rail refuses
        // the filter rather than silently returning the seeded event.
        var (success, report) = await executeAsync(new EventQueryInput
        {
            HostBuilder = martenHostBuilder(schema),
            UserNameFlag = "helen"
        });

        success.ShouldBeFalse();

        var root = report.RootElement;
        root.GetProperty("error").GetString().ShouldNotBeNull();
        root.GetProperty("error").GetString()!.ShouldContain("UserName");
        root.GetProperty("totalCount").GetInt32().ShouldBe(0);
        root.GetProperty("events").GetArrayLength().ShouldBe(0);
    }
}
