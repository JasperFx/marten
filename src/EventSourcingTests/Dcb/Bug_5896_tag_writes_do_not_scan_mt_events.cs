#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

public record Bug5896Student(Guid Value);
public record Bug5896Enrolled(string Name);

/// <summary>
/// #5896: under Quick append, HStore mode set every tagged event's tags with a follow-up
/// <c>update mt_events set tags = ... where id = $1</c>, and TagTables did the same lookup for surplus tags and
/// the per-event StartStream routes. <c>mt_events</c> has no index on <c>id</c>, so each one scanned the
/// whole table: a 62 ms decision at 1M events. The bulk function now writes HStore tags in its own insert,
/// and what still finds an event by id does so within its stream.
/// </summary>
[Collection("OneOffs")]
public class Bug_5896_tag_writes_do_not_scan_mt_events: OneOffConfigurationsContext
{
    private readonly SqlRecorder _sql = new();

    private void Configure(StoreOptions opts, DcbStorageMode mode, EventAppendMode appendMode,
        bool conjoined = false)
    {
        opts.Events.DcbStorageMode = mode;
        opts.Events.AppendMode = appendMode;
        opts.Events.AddEventType<Bug5896Enrolled>();
        opts.Events.RegisterTagType<Bug5896Student>("bug5896_student");
        if (conjoined) opts.Events.TenancyStyle = TenancyStyle.Conjoined;
    }

    private static IEvent Tagged(IDocumentSession session, object data, params object[] tags)
    {
        var e = session.Events.BuildEvent(data);
        e.WithTag(tags);
        return e;
    }

    private IEnumerable<string> tagWrites() => _sql.Statements.Where(x =>
        x.Contains("set tags", StringComparison.OrdinalIgnoreCase) ||
        x.Contains(".mt_event_tag_", StringComparison.OrdinalIgnoreCase) && x.StartsWith("insert", StringComparison.OrdinalIgnoreCase));

    [Theory]
    [InlineData(EventAppendMode.Quick)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    public async Task hstore_tags_are_written_by_the_append_itself(EventAppendMode appendMode)
    {
        StoreOptions(opts => Configure(opts, DcbStorageMode.HStore, appendMode));

        var student = new Bug5896Student(Guid.NewGuid());
        var streamId = Guid.NewGuid();

        // A new stream through Append, then the same stream again: both go through mt_quick_append_events
        await using (var session = theStore.LightweightSession())
        {
            session.Logger = _sql;
            session.Events.Append(streamId, Tagged(session, new Bug5896Enrolled("first"), student));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Logger = _sql;
            session.Events.Append(streamId, Tagged(session, new Bug5896Enrolled("second"), student),
                new Bug5896Enrolled("untagged"));
            await session.SaveChangesAsync();
        }

        tagWrites().ShouldBeEmpty();

        await using var query = theStore.QuerySession();
        var events = await query.Events.QueryByTagsAsync(new EventTagQuery().Or(student));
        events.Select(x => ((Bug5896Enrolled)x.Data).Name).ShouldBe(["first", "second"]);
    }

    [Theory]
    [InlineData(DcbStorageMode.HStore, false)]
    [InlineData(DcbStorageMode.HStore, true)]
    [InlineData(DcbStorageMode.TagTables, false)]
    [InlineData(DcbStorageMode.TagTables, true)]
    public async Task the_per_event_start_stream_route_finds_its_row_through_the_stream(DcbStorageMode mode,
        bool conjoined)
    {
        // AppendMode.Quick keeps StartStream on the per-event insert route, which writes tags afterwards
        StoreOptions(opts => Configure(opts, mode, EventAppendMode.Quick, conjoined));

        var student = new Bug5896Student(Guid.NewGuid());

        await using (var session = theStore.LightweightSession("tenant-a"))
        {
            session.Logger = _sql;
            session.Events.StartStream(Guid.NewGuid(), Tagged(session, new Bug5896Enrolled("started"), student));
            await session.SaveChangesAsync();
        }

        var writes = tagWrites().ToArray();
        writes.ShouldNotBeEmpty();
        writes.ShouldAllBe(x => x.Contains("stream_id = "));

        await using var query = theStore.QuerySession("tenant-a");
        var events = await query.Events.QueryByTagsAsync(new EventTagQuery().Or(student));
        events.Single().Data.ShouldBeOfType<Bug5896Enrolled>().Name.ShouldBe("started");
    }

    [Fact]
    public async Task surplus_tags_of_one_type_find_their_row_through_the_stream()
    {
        StoreOptions(opts => Configure(opts, DcbStorageMode.TagTables, EventAppendMode.QuickWithServerTimestamps));

        var copiedFrom = new Bug5896Student(Guid.NewGuid());
        var copier = new Bug5896Student(Guid.NewGuid());

        await using (var session = theStore.LightweightSession())
        {
            session.Logger = _sql;
            session.Events.Append(Guid.NewGuid(),
                Tagged(session, new Bug5896Enrolled("homework copied"), copiedFrom, copier));
            await session.SaveChangesAsync();
        }

        // The function writes the first tag of the type; the second goes through the by-id insert
        var writes = tagWrites().ToArray();
        writes.Length.ShouldBe(1);
        writes[0].ShouldContain("stream_id = ");

        await using var query = theStore.QuerySession();
        (await query.Events.QueryByTagsAsync(new EventTagQuery().Or(copiedFrom))).Count.ShouldBe(1);
        (await query.Events.QueryByTagsAsync(new EventTagQuery().Or(copier))).Count.ShouldBe(1);
    }

    private class SqlRecorder: IMartenSessionLogger
    {
        public List<string> Statements { get; } = new();

        public void OnBeforeExecute(NpgsqlCommand command) => Statements.Add(command.CommandText);

        public void OnBeforeExecute(NpgsqlBatch batch)
        {
            foreach (var command in batch.BatchCommands) Statements.Add(command.CommandText);
        }

        public void LogSuccess(NpgsqlCommand command) { }
        public void LogFailure(NpgsqlCommand command, Exception ex) { }
        public void LogSuccess(NpgsqlBatch batch) { }
        public void LogFailure(NpgsqlBatch batch, Exception ex) { }
        public void LogFailure(Exception ex, string message) { }
        public void RecordSavedChanges(IDocumentSession session, IChangeSet commit) { }
    }
}
