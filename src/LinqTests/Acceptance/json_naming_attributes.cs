using System;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Marten;
using Marten.Services;
using Marten.Testing.Harness;
using Newtonsoft.Json;
using Shouldly;
using Weasel.Core;

namespace LinqTests.Acceptance;

public class json_naming_attributes
{
    [Fact]
    public async Task recognize_newtonsoft_json_property_in_linq()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "atts";
        });

        using var session = store.LightweightSession();

        var command = session.Query<AttributedDoc>().Where(x => x.Color == "Red")
            .ToCommand();

        command.CommandText.ShouldBe("select d.id, d.data from atts.mt_doc_attributeddoc as d where d.data ->> 'shade' = :p0;");

    }

    [Fact]
    public async Task recognize_stj_json_property_in_linq()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "atts";
            opts.Serializer<SystemTextJsonSerializer>();
        });

        using var session = store.LightweightSession();

        var command = session.Query<StjDoc>().Where(x => x.Color == "Red")
            .ToCommand();

        command.CommandText.ShouldBe("select d.id, d.data from atts.mt_doc_stjdoc as d where d.data ->> 'shade' = :p0;");

    }

    [Fact]
    public void recognize_stj_json_property_on_datetimeoffset_in_linq()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "atts";
            opts.UseSystemTextJsonForSerialization(EnumStorage.AsInteger, Casing.Default);
        });

        using var session = store.LightweightSession();

        var cutoff = DateTimeOffset.UtcNow.AddDays(-1);
        var command = session.Query<StjDateTimeOffsetDoc>()
            .Where(x => x.Timestamp >= cutoff)
            .ToCommand();

        // The JSON key 'ts' (from [JsonPropertyName]) must appear; 'Timestamp' (C# name) must not
        command.CommandText.ShouldContain("'ts'");
        command.CommandText.ShouldNotContain("'Timestamp'");
    }

    [Fact]
    public void recognize_stj_json_property_on_datetimeoffset_required_init_in_linq()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "atts";
            opts.UseSystemTextJsonForSerialization(EnumStorage.AsInteger, Casing.Default);
        });

        using var session = store.LightweightSession();

        var cutoff = DateTimeOffset.UtcNow.AddDays(-1);
        var command = session.Query<StjRequiredInitDateTimeOffsetDoc>()
            .Where(x => x.Timestamp >= cutoff)
            .ToCommand();

        command.CommandText.ShouldContain("'ts'");
        command.CommandText.ShouldNotContain("'Timestamp'");
    }

    [Fact]
    public void recognize_stj_json_property_on_datetime_in_linq()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "atts";
            opts.UseSystemTextJsonForSerialization(EnumStorage.AsInteger, Casing.Default);
        });

        using var session = store.LightweightSession();

        var cutoff = DateTime.UtcNow.AddDays(-1);
        var command = session.Query<StjTemporalDoc>()
            .Where(x => x.DateTime >= cutoff)
            .ToCommand();

        command.CommandText.ShouldContain("'dt'");
        command.CommandText.ShouldNotContain("'DateTime'");
    }

    [Fact]
    public void recognize_stj_json_property_on_dateonly_in_linq()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "atts";
            opts.UseSystemTextJsonForSerialization(EnumStorage.AsInteger, Casing.Default);
        });

        using var session = store.LightweightSession();

        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow);
        var command = session.Query<StjTemporalDoc>()
            .Where(x => x.DateOnly >= cutoff)
            .ToCommand();

        command.CommandText.ShouldContain("'d_only'");
        command.CommandText.ShouldNotContain("'DateOnly'");
    }

    [Fact]
    public void recognize_stj_json_property_on_timeonly_in_linq()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "atts";
            opts.UseSystemTextJsonForSerialization(EnumStorage.AsInteger, Casing.Default);
        });

        using var session = store.LightweightSession();

        var cutoff = TimeOnly.FromDateTime(DateTime.UtcNow);
        var command = session.Query<StjTemporalDoc>()
            .Where(x => x.TimeOnly >= cutoff)
            .ToCommand();

        command.CommandText.ShouldContain("'t_only'");
        command.CommandText.ShouldNotContain("'TimeOnly'");
    }

    [Fact]
    public async Task end_to_end_query_with_datetimeoffset_json_property_name()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "atts_dto_e2e";
            opts.UseSystemTextJsonForSerialization(EnumStorage.AsInteger, Casing.Default);
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        await using (var session = store.LightweightSession())
        {
            session.Store(new StjDateTimeOffsetDoc
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ActorUserId = 42
            });
            await session.SaveChangesAsync();
        }

        await using (var session = store.QuerySession())
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-1);

            var totalCount = await session.Query<StjDateTimeOffsetDoc>().CountAsync();
            totalCount.ShouldBe(1);

            // The bug: this silently returns 0 even though the data matches
            var recent = await session.Query<StjDateTimeOffsetDoc>()
                .Where(x => x.Timestamp >= cutoff)
                .CountAsync();
            recent.ShouldBe(1);

            // Sanity: the int filter with JsonPropertyName works
            var byActor = await session.Query<StjDateTimeOffsetDoc>()
                .Where(x => x.ActorUserId == 42)
                .CountAsync();
            byActor.ShouldBe(1);
        }
    }
}

public class AttributedDoc
{
    public Guid Id { get; set; }

    [JsonProperty("shade")]
    public string Color { get; set; }
}

public class StjDoc
{
    public Guid Id { get; set; }

    [JsonPropertyName("shade")]
    public string Color { get; set; }
}

public class StjDateTimeOffsetDoc
{
    public Guid Id { get; set; }

    [JsonPropertyName("ts")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("auid")]
    public int ActorUserId { get; set; }
}

public class StjRequiredInitDateTimeOffsetDoc
{
    public Guid Id { get; set; }

    [JsonPropertyName("ts")]
    public required DateTimeOffset Timestamp { get; init; }

    [JsonPropertyName("auid")]
    public required int ActorUserId { get; init; }
}

public class StjTemporalDoc
{
    public Guid Id { get; set; }

    [JsonPropertyName("dt")]
    public DateTime DateTime { get; set; }

    [JsonPropertyName("dto")]
    public DateTimeOffset DateTimeOffset { get; set; }

    [JsonPropertyName("d_only")]
    public DateOnly DateOnly { get; set; }

    [JsonPropertyName("t_only")]
    public TimeOnly TimeOnly { get; set; }
}
