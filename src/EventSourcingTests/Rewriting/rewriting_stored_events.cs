using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Events.Rewriting;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Rewriting;

public class rewriting_stored_events: OneOffConfigurationsContext
{
    public record PersonRegisteredV1(string Name);

    public record PersonRegistered(string FullName);

    public record PersonMoved(string City);

    private void withUpcaster() => StoreOptions(opts =>
    {
        opts.Events.Upcast<PersonRegisteredV1, PersonRegistered>(x => new PersonRegistered(x.Name));
        opts.Events.AddEventType<PersonMoved>();
    });

    private async Task<Guid> registerAsync(object first, params object[] rest)
    {
        var streamId = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream(streamId, [first, ..rest]);
        await session.SaveChangesAsync();
        return streamId;
    }

    private async Task<List<(string Type, string Xmin)>> storedRowsAsync()
    {
        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"select type, xmin::text from {SchemaName}.mt_events order by seq_id", conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        var rows = new List<(string, string)>();
        while (await reader.ReadAsync()) rows.Add((reader.GetString(0), reader.GetString(1)));
        return rows;
    }

    [Fact]
    public async Task once_rewritten_the_upcaster_can_be_retired()
    {
        withUpcaster();
        var streamId = await registerAsync(new PersonRegisteredV1("Alice"));

        var result = await theStore.Advanced.RewriteStoredEventsAsync(x => x.IncludeType<PersonRegistered>());

        result.Databases.Single().Rewritten.ShouldBe(1);
        (await storedRowsAsync()).Single().Type.ShouldBe("person_registered");

        using var withoutUpcaster = SeparateStore(opts => opts.Events.AddEventType<PersonRegistered>());
        await using var query = withoutUpcaster.QuerySession();
        var events = await query.Events.FetchStreamAsync(streamId);
        events.Single().Data.ShouldBe(new PersonRegistered("Alice"));
    }

    [Fact]
    public async Task a_dry_run_reports_what_would_change_and_writes_nothing()
    {
        withUpcaster();
        await registerAsync(new PersonRegisteredV1("Alice"), new PersonMoved("Utrecht"));
        var before = await storedRowsAsync();

        var result = await theStore.Advanced.RewriteStoredEventsAsync(x =>
        {
            x.IncludeType<PersonRegistered>();
            x.DryRun = true;
        });

        var database = result.Databases.Single();
        database.Visited.ShouldBe(1);
        database.Rewritten.ShouldBe(1);
        database.BytesBefore.ShouldBeGreaterThan(0);
        database.BytesAfter.ShouldBeGreaterThan(0);
        (await storedRowsAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task events_already_stored_in_their_current_form_are_not_written()
    {
        withUpcaster();
        await registerAsync(new PersonRegistered("Alice"), new PersonMoved("Utrecht"));
        var before = await storedRowsAsync();

        var result = await theStore.Advanced.RewriteStoredEventsAsync(x => x.IncludeAllTypes());

        result.Databases.Single().Visited.ShouldBe(2);
        result.Databases.Single().Rewritten.ShouldBe(0);
        (await storedRowsAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task a_transform_corrects_the_stored_events()
    {
        withUpcaster();
        var streamId = await registerAsync(new PersonRegistered("Alice"), new PersonMoved("utrecht"));

        await theStore.Advanced.RewriteStoredEventsAsync(x =>
            x.Transform<PersonMoved>(e => e with { City = e.City.ToUpperInvariant() }));

        await using var query = theStore.QuerySession();
        var events = await query.Events.FetchStreamAsync(streamId);
        events.Select(e => e.Data).ShouldBe([new PersonRegistered("Alice"), new PersonMoved("UTRECHT")]);
    }

    [Fact]
    public async Task other_event_types_are_left_alone()
    {
        withUpcaster();
        await registerAsync(new PersonRegisteredV1("Alice"), new PersonMoved("Utrecht"));
        var moved = (await storedRowsAsync())[1];

        await theStore.Advanced.RewriteStoredEventsAsync(x => x.IncludeType<PersonRegistered>());

        (await storedRowsAsync())[1].ShouldBe(moved);
    }

    [Fact]
    public async Task it_runs_in_pages_and_resumes_from_a_reported_cursor()
    {
        withUpcaster();
        for (var i = 0; i < 5; i++) await registerAsync(new PersonRegisteredV1($"Person {i}"));

        var progress = new List<StoredEventRewriteProgress>();
        await theStore.Advanced.RewriteStoredEventsAsync(x =>
        {
            x.IncludeType<PersonRegistered>();
            x.BatchSize = 2;
            x.OnProgress = (p, _) =>
            {
                progress.Add(p);
                // Stop after the first page, as a crash would
                throw new InvalidOperationException("stopped");
            };
        }).ShouldThrowAsync<InvalidOperationException>();

        progress.Single().Visited.ShouldBe(2);
        (await storedRowsAsync()).Count(r => r.Type == "person_registered").ShouldBe(2);

        var result = await theStore.Advanced.RewriteStoredEventsAsync(x =>
        {
            x.IncludeType<PersonRegistered>();
            x.BatchSize = 2;
            x.ResumeFrom(progress.Single().Cursor);
        });

        result.Databases.Single().Visited.ShouldBe(3);
        result.Databases.Single().Completed.ShouldBeTrue();
        (await storedRowsAsync()).ShouldAllBe(r => r.Type == "person_registered");
    }

    [Fact]
    public async Task an_event_changed_between_the_read_and_the_write_is_skipped_not_overwritten()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<PersonMoved>();
            opts.Events.AddMaskingRuleForProtectedInformation<PersonMoved>(x => x with { City = "****" });
        });
        var streamId = await registerAsync(new PersonMoved("utrecht"));

        var result = await theStore.Advanced.RewriteStoredEventsAsync(x => x.Transform<PersonMoved>(e =>
        {
            // Runs after the page was read: masking it now is a concurrent change
            theStore.Advanced.ApplyEventDataMasking(m => m.IncludeStream(streamId)).GetAwaiter().GetResult();
            return e with { City = e.City.ToUpperInvariant() };
        }));

        result.Databases.Single().Rewritten.ShouldBe(0);
        result.Databases.Single().Skipped.ShouldBe(1);

        await using var query = theStore.QuerySession();
        var events = await query.Events.FetchStreamAsync(streamId);
        events.Single().Data.ShouldBe(new PersonMoved("****"));
    }

    [Fact]
    public async Task it_refuses_to_run_without_naming_the_events()
    {
        withUpcaster();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            theStore.Advanced.RewriteStoredEventsAsync(_ => { }));
    }
}
