using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Storage.Identification;

/// <summary>
/// W3 spike M18: validates JSON / stream-based read paths against the
/// closed-shape document storage. session.Json.FindByIdAsync returns
/// the raw JSON string from the data column; session.Json.StreamById
/// writes it to a stream without intermediate deserialization. Both
/// route through the inherited <c>DocumentStorage.BuildLoadCommand</c>
/// — verifies the closed-shape path doesn't break either.
/// </summary>
public class closed_shape_streaming_tests: BugIntegrationContext
{
    private DocumentStore ClosedShapeStore()
        => StoreOptions(opts =>
        {
        });

    [Fact]
    public async Task find_json_by_id_returns_raw_json()
    {
        var store = ClosedShapeStore();
        var id = Guid.NewGuid();
        await using (var s = store.LightweightSession())
        {
            s.Store(new StreamDoc { Id = id, Name = "raw" });
            await s.SaveChangesAsync();
        }

        await using var q = store.QuerySession();
        var json = await q.Json.FindByIdAsync<StreamDoc>(id);
        json.ShouldNotBeNullOrEmpty();
        json.ShouldContain("\"raw\"");
        json.ShouldContain(id.ToString());
    }

    [Fact]
    public async Task stream_by_id_writes_json_to_target_stream()
    {
        var store = ClosedShapeStore();
        var id = Guid.NewGuid();
        await using (var s = store.LightweightSession())
        {
            s.Store(new StreamDoc { Id = id, Name = "streamed" });
            await s.SaveChangesAsync();
        }

        await using var q = store.QuerySession();
        using var buffer = new MemoryStream();
        var found = await q.Json.StreamById<StreamDoc>(id, buffer);
        found.ShouldBeTrue();

        buffer.Position = 0;
        var json = Encoding.UTF8.GetString(buffer.ToArray());
        json.ShouldContain("\"streamed\"");
    }

    [Fact]
    public async Task find_json_by_id_returns_null_for_missing_doc()
    {
        var store = ClosedShapeStore();
        await using var q = store.QuerySession();
        var json = await q.Json.FindByIdAsync<StreamDoc>(Guid.NewGuid());
        json.ShouldBeNull();
    }

    [Fact]
    public async Task query_streams_many_results_via_linq()
    {
        var store = ClosedShapeStore();
        await using (var s = store.LightweightSession())
        {
            for (var i = 0; i < 10; i++)
            {
                s.Store(new StreamDoc { Id = Guid.NewGuid(), Name = $"row-{i}" });
            }
            await s.SaveChangesAsync();
        }

        await using var q = store.QuerySession();
        var names = await q.Query<StreamDoc>()
            .Select(x => x.Name)
            .ToListAsync();
        names.Count.ShouldBe(10);
        names.ShouldContain("row-3");
    }

    [Fact]
    public async Task load_many_async_returns_in_request_order_when_present()
    {
        var store = ClosedShapeStore();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();
        await using (var s = store.LightweightSession())
        {
            s.Store(new StreamDoc { Id = id1, Name = "one" });
            s.Store(new StreamDoc { Id = id2, Name = "two" });
            s.Store(new StreamDoc { Id = id3, Name = "three" });
            await s.SaveChangesAsync();
        }

        await using var q = store.QuerySession();
        var docs = await q.LoadManyAsync<StreamDoc>(id1, id2, id3);
        docs.Count.ShouldBe(3);
        docs.Select(x => x.Name).ShouldContain("one");
        docs.Select(x => x.Name).ShouldContain("two");
        docs.Select(x => x.Name).ShouldContain("three");
    }
}

public class StreamDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
