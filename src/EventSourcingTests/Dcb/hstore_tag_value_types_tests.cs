#nullable enable
using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

#region sample_marten_dcb_hstore_tag_value_type_records

// One wrapper record per supported simple tag value type. The HStore storage
// path stringifies every value via .ToString() on both the write side
// (EventTagOperations.BuildHstore) and the query side (HStoreDcbQueryFragment),
// so we need round-trip coverage for each primitive.
public record StringTagId(string Value);
public record GuidTagId(Guid Value);
public record IntTagId(int Value);
public record LongTagId(long Value);
public record ShortTagId(short Value);

#endregion

public record OrderEventForTagPermutations(string Note);

/// <summary>
/// Per-type round-trip coverage for <see cref="DcbStorageMode.HStore"/>. The
/// supported simple tag value types declared in <c>EventTagTable.PostgresqlTypeFor</c>
/// are <c>string</c>, <c>Guid</c>, <c>int</c>, <c>long</c>, <c>short</c>. In HStore mode
/// all five collapse to hstore text values; these tests confirm the .ToString()
/// representation on the write side matches the .ToString() representation on the
/// query side for each supported primitive, plus mixed-type OR predicates and edge
/// cases (negative ints, long boundary values, case-preserving strings).
/// </summary>
[Collection("OneOffs")]
public class hstore_tag_value_types_tests: OneOffConfigurationsContext, IAsyncLifetime
{
    public Task InitializeAsync()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<OrderEventForTagPermutations>();
            opts.Events.DcbStorageMode = DcbStorageMode.HStore;

            opts.Events.RegisterTagType<StringTagId>("string_tag");
            opts.Events.RegisterTagType<GuidTagId>("guid_tag");
            opts.Events.RegisterTagType<IntTagId>("int_tag");
            opts.Events.RegisterTagType<LongTagId>("long_tag");
            opts.Events.RegisterTagType<ShortTagId>("short_tag");
        });
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task string_tag_round_trips()
    {
        var hit = new StringTagId("hello-world");
        var miss = new StringTagId("not-the-same");

        var streamId = Guid.NewGuid();
        var ev = theSession.Events.BuildEvent(new OrderEventForTagPermutations("e1"));
        ev.WithTag(hit);
        theSession.Events.Append(streamId, ev);
        await theSession.SaveChangesAsync();

        var hitQuery = new EventTagQuery().Or<StringTagId>(hit);
        (await theSession.Events.QueryByTagsAsync(hitQuery)).Count.ShouldBe(1);

        var missQuery = new EventTagQuery().Or<StringTagId>(miss);
        (await theSession.Events.QueryByTagsAsync(missQuery)).Count.ShouldBe(0);
    }

    [Fact]
    public async Task string_tag_preserves_case_sensitivity()
    {
        // hstore values are text; "Foo" and "foo" must NOT match
        var upper = new StringTagId("CaseSensitive");
        var lower = new StringTagId("casesensitive");

        var streamId = Guid.NewGuid();
        var ev = theSession.Events.BuildEvent(new OrderEventForTagPermutations("e1"));
        ev.WithTag(upper);
        theSession.Events.Append(streamId, ev);
        await theSession.SaveChangesAsync();

        (await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or<StringTagId>(upper))).Count.ShouldBe(1);
        (await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or<StringTagId>(lower))).Count.ShouldBe(0);
    }

    [Fact]
    public async Task guid_tag_round_trips()
    {
        var hit = new GuidTagId(Guid.NewGuid());
        var miss = new GuidTagId(Guid.NewGuid());

        var streamId = Guid.NewGuid();
        var ev = theSession.Events.BuildEvent(new OrderEventForTagPermutations("e1"));
        ev.WithTag(hit);
        theSession.Events.Append(streamId, ev);
        await theSession.SaveChangesAsync();

        (await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or<GuidTagId>(hit))).Count.ShouldBe(1);
        (await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or<GuidTagId>(miss))).Count.ShouldBe(0);
    }

    [Fact]
    public async Task int_tag_round_trips()
    {
        var hit = new IntTagId(42);
        var miss = new IntTagId(43);

        var streamId = Guid.NewGuid();
        var ev = theSession.Events.BuildEvent(new OrderEventForTagPermutations("e1"));
        ev.WithTag(hit);
        theSession.Events.Append(streamId, ev);
        await theSession.SaveChangesAsync();

        (await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or<IntTagId>(hit))).Count.ShouldBe(1);
        (await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or<IntTagId>(miss))).Count.ShouldBe(0);
    }

    [Fact]
    public async Task int_tag_negative_value_round_trips()
    {
        // Stringification of negative integers must be consistent across write + read.
        var hit = new IntTagId(-2_147_483_648); // int.MinValue — edge case
        var miss = new IntTagId(-1);

        var streamId = Guid.NewGuid();
        var ev = theSession.Events.BuildEvent(new OrderEventForTagPermutations("e1"));
        ev.WithTag(hit);
        theSession.Events.Append(streamId, ev);
        await theSession.SaveChangesAsync();

        (await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or<IntTagId>(hit))).Count.ShouldBe(1);
        (await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or<IntTagId>(miss))).Count.ShouldBe(0);
    }

    [Fact]
    public async Task long_tag_round_trips()
    {
        var hit = new LongTagId(long.MaxValue); // boundary value
        var miss = new LongTagId(long.MaxValue - 1);

        var streamId = Guid.NewGuid();
        var ev = theSession.Events.BuildEvent(new OrderEventForTagPermutations("e1"));
        ev.WithTag(hit);
        theSession.Events.Append(streamId, ev);
        await theSession.SaveChangesAsync();

        (await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or<LongTagId>(hit))).Count.ShouldBe(1);
        (await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or<LongTagId>(miss))).Count.ShouldBe(0);
    }

    [Fact]
    public async Task short_tag_round_trips()
    {
        var hit = new ShortTagId(short.MaxValue); // boundary value
        var miss = new ShortTagId((short)(short.MaxValue - 1));

        var streamId = Guid.NewGuid();
        var ev = theSession.Events.BuildEvent(new OrderEventForTagPermutations("e1"));
        ev.WithTag(hit);
        theSession.Events.Append(streamId, ev);
        await theSession.SaveChangesAsync();

        (await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or<ShortTagId>(hit))).Count.ShouldBe(1);
        (await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or<ShortTagId>(miss))).Count.ShouldBe(0);
    }

    [Fact]
    public async Task mixed_tag_types_in_single_or_query()
    {
        // One event carries one tag of every supported type; a single OR query
        // referencing all five should find it.
        var s = new StringTagId("anchor");
        var g = new GuidTagId(Guid.NewGuid());
        var i = new IntTagId(123);
        var l = new LongTagId(456L);
        var h = new ShortTagId((short)789);

        var streamId = Guid.NewGuid();
        var ev = theSession.Events.BuildEvent(new OrderEventForTagPermutations("mixed"));
        ev.WithTag(s, g, i, l, h);
        theSession.Events.Append(streamId, ev);
        await theSession.SaveChangesAsync();

        var query = new EventTagQuery()
            .Or<StringTagId>(s)
            .Or<GuidTagId>(g)
            .Or<IntTagId>(i)
            .Or<LongTagId>(l)
            .Or<ShortTagId>(h);

        var events = await theSession.Events.QueryByTagsAsync(query);
        events.Count.ShouldBe(1);

        // Each tag type alone is also a sufficient predicate.
        (await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or<StringTagId>(s))).Count.ShouldBe(1);
        (await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or<GuidTagId>(g))).Count.ShouldBe(1);
        (await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or<IntTagId>(i))).Count.ShouldBe(1);
        (await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or<LongTagId>(l))).Count.ShouldBe(1);
        (await theSession.Events.QueryByTagsAsync(new EventTagQuery().Or<ShortTagId>(h))).Count.ShouldBe(1);
    }

    [Fact]
    public async Task events_exist_async_across_tag_types()
    {
        // Validate the EXISTS path for each type — this is the DCB consistency-check
        // SQL shape and the hottest read path under load.
        var s = new StringTagId("exists-string");
        var g = new GuidTagId(Guid.NewGuid());
        var i = new IntTagId(7);
        var l = new LongTagId(11L);
        var h = new ShortTagId((short)13);

        var streamId = Guid.NewGuid();
        var ev = theSession.Events.BuildEvent(new OrderEventForTagPermutations("exists"));
        ev.WithTag(s, g, i, l, h);
        theSession.Events.Append(streamId, ev);
        await theSession.SaveChangesAsync();

        (await theSession.Events.EventsExistAsync(new EventTagQuery().Or<StringTagId>(s))).ShouldBeTrue();
        (await theSession.Events.EventsExistAsync(new EventTagQuery().Or<GuidTagId>(g))).ShouldBeTrue();
        (await theSession.Events.EventsExistAsync(new EventTagQuery().Or<IntTagId>(i))).ShouldBeTrue();
        (await theSession.Events.EventsExistAsync(new EventTagQuery().Or<LongTagId>(l))).ShouldBeTrue();
        (await theSession.Events.EventsExistAsync(new EventTagQuery().Or<ShortTagId>(h))).ShouldBeTrue();

        // Non-matching values of the same type must miss.
        (await theSession.Events.EventsExistAsync(new EventTagQuery().Or<StringTagId>(new StringTagId("nope")))).ShouldBeFalse();
        (await theSession.Events.EventsExistAsync(new EventTagQuery().Or<IntTagId>(new IntTagId(8)))).ShouldBeFalse();
    }
}
