#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Marten;
using Marten.Newtonsoft;
using Marten.Patching;
using Marten.Testing.Harness;
using Newtonsoft.Json;
using Shouldly;
using Xunit;

namespace PatchingTests.Patching;

/// <summary>
/// #5290. The patching API built its JSON paths from raw C# member names with only a casing transform,
/// so a member carrying a serializer alias — <c>[JsonPropertyName]</c>, or <c>[JsonProperty]</c> through
/// the Marten.Newtonsoft resolver — was patched at a path the serializer never reads.
/// <para>
/// The failure is silent in every direction that matters. The patch reports success, <c>Load</c> and
/// <c>Query</c> keep returning the old value, and the phantom node sits beside the real one until the
/// next full document save erases it. The reporter ran a five-second heartbeat into a phantom node for
/// weeks with no error anywhere. So these tests assert on the stored JSON as well as on the round-trip:
/// "the value came back changed" alone would not have caught the original bug in the nested case, and
/// nothing at all catches a second node appearing beside the right one.
/// </para>
/// </summary>
public class Bug_5290_patch_honors_serializer_member_aliases: OneOffConfigurationsContext
{
    public class AliasedDoc
    {
        public Guid Id { get; set; }

        [JsonPropertyName("nm")]
        [JsonProperty("nm")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("jb")]
        [JsonProperty("jb")]
        public JobState? Job { get; set; }

        [JsonPropertyName("cnt")]
        [JsonProperty("cnt")]
        public int Count { get; set; }

        [JsonPropertyName("tg")]
        [JsonProperty("tg")]
        public List<string> Tags { get; set; } = new();

        public Dictionary<string, string> Values { get; set; } = new();
    }

    public class JobState
    {
        [JsonPropertyName("pr")]
        [JsonProperty("pr")]
        public int Progress { get; set; }
    }

    private void UseSystemTextJson() => StoreOptions(opts => opts.UseSystemTextJsonForSerialization());

    private void UseNewtonsoft() => StoreOptions(opts => opts.UseNewtonsoftForSerialization());

    private async Task<Guid> SeedAsync(Action<AliasedDoc>? configure = null)
    {
        var doc = new AliasedDoc { Id = Guid.NewGuid(), Name = "original", Job = new JobState { Progress = 1 } };
        configure?.Invoke(doc);

        await using var session = theStore.LightweightSession();
        session.Store(doc);
        await session.SaveChangesAsync();

        return doc.Id;
    }

    /// <summary>
    /// The raw stored JSON, which is the only place the phantom node is visible. Reading through
    /// <c>LoadAsync</c> cannot see it — that is exactly why the bug survived so long in production.
    /// </summary>
    private async Task<JsonElement> RawJsonAsync(Guid id)
    {
        await using var session = theStore.QuerySession();
        var json = await session.Json.FindByIdAsync<AliasedDoc>(id);
        json.ShouldNotBeNull();

        return JsonDocument.Parse(json!).RootElement.Clone();
    }

    [Fact]
    public async Task set_on_an_aliased_member_under_system_text_json()
    {
        UseSystemTextJson();
        var id = await SeedAsync();

        await using (var session = theStore.LightweightSession())
        {
            session.Patch<AliasedDoc>(id).Set(x => x.Name, "changed");
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            (await session.LoadAsync<AliasedDoc>(id))!.Name.ShouldBe("changed");
        }

        var json = await RawJsonAsync(id);
        json.GetProperty("nm").GetString().ShouldBe("changed");
        // The half a round-trip assertion cannot see: pre-fix the patch wrote a second node here.
        json.TryGetProperty("Name", out _).ShouldBeFalse("a phantom node was written beside the real one");
    }

    [Fact]
    public async Task set_on_an_aliased_member_under_newtonsoft()
    {
        // Marten.Newtonsoft registers its own [JsonProperty] resolver, so this is a genuinely separate
        // route through ToJsonKey rather than the same assertion twice.
        UseNewtonsoft();
        var id = await SeedAsync();

        await using (var session = theStore.LightweightSession())
        {
            session.Patch<AliasedDoc>(id).Set(x => x.Name, "changed");
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            (await session.LoadAsync<AliasedDoc>(id))!.Name.ShouldBe("changed");
        }

        var json = await RawJsonAsync(id);
        json.GetProperty("nm").GetString().ShouldBe("changed");
        json.TryGetProperty("Name", out _).ShouldBeFalse("a phantom node was written beside the real one");
    }

    [Fact]
    public async Task set_on_a_nested_aliased_member()
    {
        // The reporter's actual shape: a heartbeat writing data->'Job'->'Progress' while every reader
        // looked at data->'jb'->'pr'. Both segments of the path have to resolve.
        UseSystemTextJson();
        var id = await SeedAsync();

        await using (var session = theStore.LightweightSession())
        {
            session.Patch<AliasedDoc>(id).Set(x => x.Job!.Progress, 42);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            (await session.LoadAsync<AliasedDoc>(id))!.Job!.Progress.ShouldBe(42);
        }

        var json = await RawJsonAsync(id);
        json.GetProperty("jb").GetProperty("pr").GetInt32().ShouldBe(42);
        json.TryGetProperty("Job", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task increment_and_append_also_resolve_the_alias()
    {
        // Every patch operation goes through the same toPath, but Set is the only one the issue names.
        // Two more, because a path helper that only worked for Set would still be a bug.
        UseSystemTextJson();
        var id = await SeedAsync(doc => doc.Count = 5);

        await using (var session = theStore.LightweightSession())
        {
            session.Patch<AliasedDoc>(id).Increment(x => x.Count, 3);
            session.Patch<AliasedDoc>(id).Append(x => x.Tags, "first");
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            var reloaded = (await session.LoadAsync<AliasedDoc>(id))!;
            reloaded.Count.ShouldBe(8);
            reloaded.Tags.ShouldHaveTheSameElementsAs("first");
        }

        var json = await RawJsonAsync(id);
        json.TryGetProperty("Count", out _).ShouldBeFalse();
        json.TryGetProperty("Tags", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task a_dictionary_key_is_still_used_verbatim()
    {
        // Guard against the fix over-reaching. Dictionary keys reach toPath as IndexerKeyInfo, a
        // synthetic MemberInfo that declares no attributes — the key is data, not a member name, and
        // must not be run through alias resolution.
        UseSystemTextJson();

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Store(new AliasedDoc { Id = id, Values = new Dictionary<string, string> { ["Name"] = "original" } });
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Patch<AliasedDoc>(id).Set(x => x.Values["Name"], "changed");
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            (await session.LoadAsync<AliasedDoc>(id))!.Values["Name"].ShouldBe("changed");
        }
    }

    [Fact]
    public async Task remove_by_predicate_resolves_aliases_in_the_json_path()
    {
        // The other path builder in PatchExpression: the predicate overloads go through JsonPathCreator
        // rather than toPath, and it had the same gap. A JSONPath predicate naming the unaliased member
        // matches nothing, so the element is silently left in place.
        UseSystemTextJson();

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Store(new AliasedDoc
            {
                Id = id,
                Job = new JobState { Progress = 1 },
                Tags = ["keep", "drop"]
            });
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Patch<AliasedDoc>(id).Remove(x => x.Tags, "drop");
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            (await session.LoadAsync<AliasedDoc>(id))!.Tags.ShouldHaveTheSameElementsAs("keep");
        }
    }
}
