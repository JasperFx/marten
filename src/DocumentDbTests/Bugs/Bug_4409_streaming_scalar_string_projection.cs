using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Weasel.Core;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

// Regression for https://github.com/JasperFx/marten/issues/4409.
//
// `WriteArray` / `StreamJsonArray` on a scalar string projection (e.g.
// `.Select(x => x.SomeEnumProperty)` with `EnumStorage.AsString`, or any
// `.Select(x => x.StringProperty)`) emitted unquoted values into the JSON
// array — `[FooValue,BarValue]` instead of `["FooValue","BarValue"]`.
// Root cause: postgres returns `data->>'X'` as raw text (no JSON quoting),
// and the streaming path used to copy those raw bytes between commas
// without per-value JSON encoding. Fix lifts JSON encoding into the
// streaming extension when the projected column isn't already jsonb/json.
//
// The tests below stream both string and enum-as-string scalar projections
// and assert the body parses as valid JSON via System.Text.Json.
public class Bug_4409_streaming_scalar_string_projection: BugIntegrationContext
{
    public enum Mood
    {
        Happy,
        Sad,
        Curious
    }

    public class MoodDoc
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public Mood SomeMood { get; set; }
        public int? SomeNumber { get; set; }
    }

    private async Task<string> seed(EnumStorage enumStorage)
    {
        var schemaName = "bug4409_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        StoreOptions(opts =>
        {
            opts.UseSystemTextJsonForSerialization(enumStorage: enumStorage);
            opts.DatabaseSchemaName = schemaName;
        });

        await using var session = theStore.LightweightSession();
        session.Store(new MoodDoc { Name = "alice", SomeMood = Mood.Happy, SomeNumber = 1 });
        session.Store(new MoodDoc { Name = "bob", SomeMood = Mood.Sad, SomeNumber = 2 });
        session.Store(new MoodDoc { Name = "carol", SomeMood = Mood.Happy, SomeNumber = 3 });
        await session.SaveChangesAsync();
        return schemaName;
    }

    [Fact]
    public async Task stream_array_of_enum_as_string_projection_emits_valid_json()
    {
        await seed(EnumStorage.AsString);

        var stream = new MemoryStream();
        await using var query = theStore.QuerySession();
        await query.Query<MoodDoc>()
            .Select(x => x.SomeMood)
            .Distinct()
            .StreamJsonArray(stream);

        stream.Position = 0;
        var body = Encoding.UTF8.GetString(stream.ToArray());

        // Should parse as a JSON array — pre-fix it was `[Happy,Sad]`
        // which surfaces as `'H' is an invalid start of a value`.
        var moods = JsonSerializer.Deserialize<string[]>(body);
        moods.ShouldNotBeNull();
        moods!.OrderBy(x => x).ShouldBe(new[] { "Happy", "Sad" });
    }

    [Fact]
    public async Task stream_array_of_string_projection_emits_valid_json()
    {
        await seed(EnumStorage.AsString);

        var stream = new MemoryStream();
        await using var query = theStore.QuerySession();
        await query.Query<MoodDoc>()
            .Select(x => x.Name)
            .StreamJsonArray(stream);

        stream.Position = 0;
        var body = Encoding.UTF8.GetString(stream.ToArray());

        var names = JsonSerializer.Deserialize<string[]>(body);
        names.ShouldNotBeNull();
        names!.OrderBy(x => x).ShouldBe(new[] { "alice", "bob", "carol" });
    }

    [Fact]
    public async Task stream_array_of_string_projection_escapes_embedded_special_characters()
    {
        // Strings with quotes / backslashes / newlines must be JSON-escaped so the
        // streamed body is still parseable. Pre-fix the raw text-with-quotes would
        // also fail STJ parse even before #4409's missing-quotes issue.
        var schemaName = "bug4409_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        StoreOptions(opts =>
        {
            opts.UseSystemTextJsonForSerialization();
            opts.DatabaseSchemaName = schemaName;
        });

        await using var session = theStore.LightweightSession();
        session.Store(new MoodDoc { Name = "alice \"in wonderland\"" });
        session.Store(new MoodDoc { Name = "bob\\backslash" });
        session.Store(new MoodDoc { Name = "newline\ninside" });
        await session.SaveChangesAsync();

        var stream = new MemoryStream();
        await using var query = theStore.QuerySession();
        await query.Query<MoodDoc>()
            .Select(x => x.Name)
            .StreamJsonArray(stream);

        stream.Position = 0;
        var body = Encoding.UTF8.GetString(stream.ToArray());

        var names = JsonSerializer.Deserialize<string[]>(body);
        names.ShouldNotBeNull();
        names!.Length.ShouldBe(3);
        names.ShouldContain("alice \"in wonderland\"");
        names.ShouldContain("bob\\backslash");
        names.ShouldContain("newline\ninside");
    }

    [Fact]
    public async Task stream_array_of_int_projection_still_emits_valid_json()
    {
        // Numeric projection already produced valid JSON (postgres returns raw
        // digits, which happens to be a valid JSON literal). Pin it as a
        // regression guard — the #4409 fix must not break the numeric path.
        await seed(EnumStorage.AsString);

        var stream = new MemoryStream();
        await using var query = theStore.QuerySession();
        await query.Query<MoodDoc>()
            .OrderBy(x => x.SomeNumber)
            .Select(x => x.SomeNumber)
            .StreamJsonArray(stream);

        stream.Position = 0;
        var body = Encoding.UTF8.GetString(stream.ToArray());

        var numbers = JsonSerializer.Deserialize<int[]>(body);
        numbers.ShouldNotBeNull();
        numbers!.ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task stream_array_of_jsonb_documents_still_emits_valid_json()
    {
        // The whole-document streaming path (jsonb column) is the non-regression
        // case — make sure the per-value JSON encoding the fix introduces only
        // kicks in for non-jsonb projections.
        await seed(EnumStorage.AsString);

        var stream = new MemoryStream();
        await using var query = theStore.QuerySession();
        await query.Query<MoodDoc>().StreamJsonArray(stream);

        stream.Position = 0;
        var body = Encoding.UTF8.GetString(stream.ToArray());

        var docs = JsonSerializer.Deserialize<MoodDoc[]>(body, new JsonSerializerOptions
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            PropertyNameCaseInsensitive = true
        });
        docs.ShouldNotBeNull();
        docs!.Length.ShouldBe(3);
    }
}
