#nullable enable
using System;
using System.Reflection;
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
/// <c>RegisterTagType&lt;TTag, TInner&gt;(unwrapper, tableSuffix)</c>, the overload that works under trimming
/// and Native AOT, and the diagnostics that point at it. The inferring overloads reflect over the tag's
/// members and close <c>TagTypeRegistration&lt;TTag, TInner&gt;</c> with <c>MakeGenericType</c>, neither of
/// which a native image can do. Under a JIT both overloads must register the same thing, and everything
/// downstream of the registration -- the tag tables, tag queries and the consistency boundary -- must not be
/// able to tell them apart. The native half is <c>Marten.AotRuntimeSmoke</c>.
/// </summary>
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
    public void a_null_suffix_takes_the_same_default_as_the_inferring_overload()
    {
        // The suffix is optional precisely so that the AOT-safe overload is not more verbose than it has
        // to be; null has to mean the same table the inferring overload would have named.
        new StoreOptions().EventGraph.RegisterTagType<EnrolleeId, Guid>(x => x.Value)
            .TableSuffix.ShouldBe(new StoreOptions().EventGraph.RegisterTagType<EnrolleeId>().TableSuffix);
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
        // Silently keeping the first would leave this call's unwrapper unused, so every tag value would be
        // read by the other registration's rule
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
    [InlineData("")]
    [InlineData("   ")]
    public void an_empty_table_suffix_is_refused_on_the_explicit_overload(string tableSuffix)
    {
        // It would name mt_event_tag_, a table every tag type registered that way would share
        Should.Throw<ArgumentException>(() =>
            new StoreOptions().EventGraph.RegisterTagType<EnrolleeId, Guid>(x => x.Value, tableSuffix));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void an_empty_table_suffix_is_refused_on_the_inferring_overload_too(string tableSuffix)
    {
        // BEHAVIOUR CHANGE: this used to be accepted and name mt_event_tag_
        Should.Throw<ArgumentException>(() =>
            new StoreOptions().EventGraph.RegisterTagType<EnrolleeId>(tableSuffix));
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
    public void a_generic_that_cannot_be_closed_natively_names_the_explicit_overload()
    {
        var failure = new NotSupportedException("missing native code or metadata");

        var ex = Should.Throw<InvalidOperationException>(() =>
            EventGraph.CreateTagType(typeof(EnrolleeId), "enrollee", _ => throw failure, canEmit: false));

        ex.Message.ShouldContain("opts.Events.RegisterTagType<EnrolleeId, Guid>(x => x.Value, \"enrollee\");");
        ex.Message.ShouldContain("Native AOT");
        ex.InnerException.ShouldBeSameAs(failure);
    }

    [Fact]
    public void a_missing_constructor_natively_names_the_explicit_overload_too()
    {
        var failure = new MissingMethodException("no parameterless constructor");

        Should.Throw<InvalidOperationException>(() =>
                EventGraph.CreateTagType(typeof(EnrolleeId), "enrollee", _ => throw failure, canEmit: false))
            .InnerException.ShouldBeSameAs(failure);
    }

    [Fact]
    public void an_unreadable_tag_names_the_explicit_overload_under_a_jit_as_well()
    {
        // #5649 follow-up. InvalidValueTypeException is ambiguous: the tag may genuinely not be a
        // single-value wrapper, or a trimmed image may have lost the members that say so. A trimmed
        // non-AOT image still has dynamic code, so no runtime flag separates the two -- and the explicit
        // overload is the answer either way, so it is named either way.
        var failure = new InvalidValueTypeException(typeof(EnrolleeId), "Must be only a single public, 'gettable' property");

        foreach (var canEmit in new[] { true, false })
        {
            var ex = Should.Throw<InvalidOperationException>(() =>
                EventGraph.CreateTagType(typeof(EnrolleeId), "enrollee", _ => throw failure, canEmit));

            ex.Message.ShouldContain("opts.Events.RegisterTagType<EnrolleeId, Guid>(x => x.Value, \"enrollee\");");
            ex.Message.ShouldContain("trimmed");
            ex.InnerException.ShouldBeSameAs(failure);
        }
    }

    [Fact]
    public void an_unrelated_failure_is_not_reinterpreted()
    {
        var failure = new ArgumentException("something else");

        Should.Throw<ArgumentException>(() =>
                EventGraph.CreateTagType(typeof(EnrolleeId), null, _ => throw failure, canEmit: false))
            .ShouldBeSameAs(failure);
    }

    [Fact]
    public void a_closing_failure_under_a_jit_is_not_reinterpreted()
    {
        // Unlike the unreadable-tag case above, this one is unambiguous: a JIT closed the generic, so a
        // NotSupportedException from inside the registration is about something else entirely.
        var failure = new NotSupportedException("something else");

        Should.Throw<NotSupportedException>(() =>
                EventGraph.CreateTagType(typeof(EnrolleeId), null, _ => throw failure, canEmit: true))
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
    public void the_remedy_follows_the_member_name()
    {
        EventGraph.TagTypeRegistrationLine(typeof(CodeTagId), "code", null)
            .ShouldBe("opts.Events.RegisterTagType<CodeTagId, string>(x => x.Code, \"code\");");
    }

    [Fact]
    public void an_unreadable_identity_is_tolerated_quietly_under_a_jit()
    {
        // The ordinary case the discovery pass exists to walk past: a projection whose identity is simply
        // not a strong-typed identifier. A warning here would fire for most projections.
        var wrapped = new TargetInvocationException(
            new InvalidValueTypeException(typeof(TwoPropertyTagId), "Must be only a single public, 'gettable' property"));

        EventGraph.IsToleratedDiscoveryFailure(wrapped, canEmit: true).ShouldBeTrue();
    }

    [Fact]
    public void an_unreadable_identity_is_NOT_tolerated_quietly_with_no_jit()
    {
        // #5649 follow-up: natively, the same exception means the tag's members were trimmed away, which is
        // a tag type silently going unregistered -- the failure #5649 actually reported, and the one that
        // used to be the only one with no warning.
        var wrapped = new TargetInvocationException(
            new InvalidValueTypeException(typeof(EnrolleeId), "Must be only a single public, 'gettable' property"));

        EventGraph.IsToleratedDiscoveryFailure(wrapped, canEmit: false).ShouldBeFalse();
    }

    [Fact]
    public void a_closing_failure_is_never_tolerated_quietly()
    {
        EventGraph.IsToleratedDiscoveryFailure(new NotSupportedException("missing native code or metadata"), canEmit: true)
            .ShouldBeFalse();
        EventGraph.IsToleratedDiscoveryFailure(new NotSupportedException("missing native code or metadata"), canEmit: false)
            .ShouldBeFalse();
    }

    [Fact]
    public void the_discovery_line_keeps_the_aggregate()
    {
        // Auto-discovery registers the tag for the projection's aggregate, and a boundary append routes by it
        EventGraph.TagTypeRegistrationLine(typeof(EnrolleeId), null, typeof(SubscriptionState))
            .ShouldBe("opts.Events.RegisterTagType<EnrolleeId, Guid>(x => x.Value, \"enrollee_id\").ForAggregate<SubscriptionState>();");
    }
}

public record CodeTagId(string Code);

public record TwoPropertyTagId(Guid Value, string Label);
