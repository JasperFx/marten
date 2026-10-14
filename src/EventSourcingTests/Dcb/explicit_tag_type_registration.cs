#nullable enable
using System;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

/// <summary>
/// <c>RegisterTagType&lt;TTag, TInner&gt;(unwrapper, tableSuffix)</c>, the overload that works under Native AOT. The
/// inferring overloads close <c>TagTypeRegistration&lt;TTag, TInner&gt;</c> with <c>MakeGenericType</c>,
/// which a native image has no code for. Under a JIT both must register the same thing, and everything
/// downstream of the registration -- the tag tables, tag queries and the consistency boundary -- must
/// not be able to tell them apart. The native half is <c>Marten.AotRuntimeSmoke</c>.
/// </summary>
[Collection("OneOffs")]
public class explicit_tag_type_registration: OneOffConfigurationsContext
{
    [Fact]
    public void registers_the_same_shape_as_the_inferring_overload()
    {
        var inferred = new StoreOptions().EventGraph.RegisterTagType<EnrolleeId>();
        var explicitly = new StoreOptions().EventGraph.RegisterTagType<EnrolleeId, Guid>(x => x.Value, inferred.TableSuffix);

        explicitly.TagType.ShouldBe(inferred.TagType);
        explicitly.SimpleType.ShouldBe(inferred.SimpleType);
        explicitly.TableSuffix.ShouldBe(inferred.TableSuffix);
    }

    [Fact]
    public void honours_a_custom_table_suffix()
    {
        new StoreOptions().EventGraph.RegisterTagType<StringTagId, string>(x => x.Value, "custom")
            .TableSuffix.ShouldBe("custom");
    }

    [Fact]
    public void the_first_registration_of_a_tag_type_wins_either_way()
    {
        var events = new StoreOptions().EventGraph;

        var first = events.RegisterTagType<EnrolleeId, Guid>(x => x.Value, "enrollee");
        events.RegisterTagType<EnrolleeId>("other").ShouldBeSameAs(first);
        events.RegisterTagType<EnrolleeId, Guid>(x => x.Value, "another").ShouldBeSameAs(first);

        events.TagTypes.Count.ShouldBe(1);
    }

    [Fact]
    public void an_explicit_registration_after_an_inferred_one_returns_the_first()
    {
        var events = new StoreOptions().EventGraph;

        var first = events.RegisterTagType<EnrolleeId>("enrollee");
        events.RegisterTagType<EnrolleeId, Guid>(x => x.Value, "another").ShouldBeSameAs(first);

        events.TagTypes.Count.ShouldBe(1);
    }

    [Fact]
    public void a_conflicting_inner_type_is_refused()
    {
        var events = new StoreOptions().EventGraph;
        events.RegisterTagType<EnrolleeId>("enrollee");

        Should.Throw<InvalidOperationException>(() =>
                events.RegisterTagType<EnrolleeId, string>(x => x.Value.ToString(), "enrollee"))
            .Message.ShouldContain("already registered with the inner type System.Guid");
    }

    [Fact]
    public void unwrapper_is_required()
    {
        Should.Throw<ArgumentNullException>(() =>
            new StoreOptions().EventGraph.RegisterTagType<EnrolleeId, Guid>(null!, "enrollee"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void table_suffix_is_required(string? tableSuffix)
    {
        // An empty suffix would name the tag table mt_event_tag_
        Should.Throw<ArgumentException>(() =>
            new StoreOptions().EventGraph.RegisterTagType<EnrolleeId, Guid>(x => x.Value, tableSuffix!));
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task tags_queries_and_the_boundary_work_from_an_explicit_registration(DcbStorageMode mode)
    {
        StoreOptions(opts =>
        {
            opts.Events.DcbStorageMode = mode;
            opts.Events.AddEventType<Enrolled>();
            opts.Events.AddEventType<ProgressRecorded>();

            opts.Events.RegisterTagType<EnrolleeId, Guid>(x => x.Value, "enrollee").ForAggregate<SubscriptionState>();
            opts.Events.RegisterTagType<StringTagId, string>(x => x.Value, "string_tag").ForAggregate<SubscriptionState>();
        });

        var enrollee = new EnrolleeId(Guid.NewGuid());
        var cohort = new StringTagId("cohort-" + Guid.NewGuid().ToString("N"));

        await using (var session = theStore.LightweightSession())
        {
            var enrolled = session.Events.BuildEvent(new Enrolled("Alice"));
            enrolled.WithTag(enrollee, cohort);
            session.Events.Append(Guid.NewGuid(), enrolled);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            (await session.Events.QueryByTagsAsync(new EventTagQuery().Or<EnrolleeId>(enrollee))).Count.ShouldBe(1);
            (await session.Events.QueryByTagsAsync(new EventTagQuery().Or<StringTagId>(cohort))).Count.ShouldBe(1);

            var boundary = await session.Events.FetchForWritingByTags<SubscriptionState>(
                new EventTagQuery().Or<EnrolleeId>(enrollee));
            boundary.Aggregate!.EnrollmentCount.ShouldBe(1);

            var progress = session.Events.BuildEvent(new ProgressRecorded("week 1"));
            progress.WithTag(enrollee);
            boundary.AppendOne(progress);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            var state = await session.Events.AggregateByTagsAsync<SubscriptionState>(
                new EventTagQuery().Or<EnrolleeId>(enrollee));
            state!.ProgressCount.ShouldBe(1);
        }
    }

    [Fact]
    public void an_inferred_registration_that_fails_under_aot_names_the_explicit_overload()
    {
        var failure = new NotSupportedException("missing native code or metadata");

        var ex = Should.Throw<InvalidOperationException>(() =>
            EventGraph.CreateTagType(typeof(EnrolleeId), "enrollee", _ => throw failure, canEmit: false));

        ex.Message.ShouldContain("opts.Events.RegisterTagType<EnrolleeId, Guid>(x => x.Value, \"enrollee\");");
        ex.InnerException.ShouldBeSameAs(failure);
    }

    [Fact]
    public void a_missing_constructor_under_aot_names_the_explicit_overload_too()
    {
        var failure = new MissingMethodException("no parameterless constructor");

        Should.Throw<InvalidOperationException>(() =>
                EventGraph.CreateTagType(typeof(EnrolleeId), "enrollee", _ => throw failure, canEmit: false))
            .InnerException.ShouldBeSameAs(failure);
    }

    [Fact]
    public void trimmed_tag_metadata_under_aot_names_the_explicit_overload_too()
    {
        // An app that does not root its own assembly loses the tag's members, and ValueTypeInfo then refuses
        // the tag as a value type. The explicit overload needs none of that metadata.
        var failure = new InvalidValueTypeException(typeof(EnrolleeId), "Must be only a single public, 'gettable' property");

        Should.Throw<InvalidOperationException>(() =>
                EventGraph.CreateTagType(typeof(EnrolleeId), "enrollee", _ => throw failure, canEmit: false))
            .InnerException.ShouldBeSameAs(failure);
    }

    [Fact]
    public void an_unrelated_failure_under_aot_is_not_reinterpreted()
    {
        var failure = new ArgumentException("something else");

        Should.Throw<ArgumentException>(() =>
                EventGraph.CreateTagType(typeof(EnrolleeId), null, _ => throw failure, canEmit: false))
            .ShouldBeSameAs(failure);
    }

    [Fact]
    public void the_remedy_uses_placeholders_when_the_tag_cannot_be_read()
    {
        EventGraph.TagTypeRegistrationLine(typeof(TwoPropertyTagId), "two", null)
            .ShouldBe("opts.Events.RegisterTagType<TwoPropertyTagId, TInner>(x => x.Value, \"two\");");
    }

    [Fact]
    public void the_remedy_follows_the_inner_type_and_supplies_the_default_suffix()
    {
        // A string-backed id has to get a line that compiles, not a hard-coded Guid, and the suffix the
        // inferring overload would have used keeps the same tag table
        EventGraph.TagTypeRegistrationLine(typeof(StringTagId), null, null)
            .ShouldBe("opts.Events.RegisterTagType<StringTagId, string>(x => x.Value, \"string_tag_id\");");
    }

    [Fact]
    public void the_remedy_replaces_an_empty_suffix_with_the_default()
    {
        // The inferring overload accepts "", which names no usable table; the explicit overload refuses it
        EventGraph.TagTypeRegistrationLine(typeof(StringTagId), "", null)
            .ShouldBe("opts.Events.RegisterTagType<StringTagId, string>(x => x.Value, \"string_tag_id\");");
    }

    [Fact]
    public void the_discovery_line_keeps_the_aggregate()
    {
        // Auto-discovery registers the tag for the projection's aggregate, and a boundary append routes by it
        EventGraph.TagTypeRegistrationLine(typeof(EnrolleeId), null, typeof(SubscriptionState))
            .ShouldBe("opts.Events.RegisterTagType<EnrolleeId, Guid>(x => x.Value, \"enrollee_id\").ForAggregate<SubscriptionState>();");
    }

    [Fact]
    public void the_remedy_follows_the_member_name()
    {
        EventGraph.TagTypeRegistrationLine(typeof(CodeTagId), "code", null)
            .ShouldBe("opts.Events.RegisterTagType<CodeTagId, string>(x => x.Code, \"code\");");
    }

    [Fact]
    public void a_failure_under_a_jit_is_not_reinterpreted()
    {
        var failure = new NotSupportedException("something else");

        Should.Throw<NotSupportedException>(() =>
                EventGraph.CreateTagType(typeof(EnrolleeId), null, _ => throw failure, canEmit: true))
            .ShouldBeSameAs(failure);
    }
}

public record CodeTagId(string Code);

public record TwoPropertyTagId(Guid Value, string Label);
