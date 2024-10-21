using System;
using System.Threading.Tasks;
using EventSourcingTests.Projections;
using Marten.Events;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests;

public class asserting_on_expected_event_version_on_append: IntegrationContext
{
    private readonly ITestOutputHelper _output;

    [Fact]
    public async Task should_check_max_event_id_on_append()
    {
        var joined = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        var stream = theSession.Events.StartStream<Quest>(joined).Id;
        theSession.Events.Append(stream, 2, departed);

        await theSession.SaveChangesAsync();

        var state = theSession.Events.FetchStreamState(stream);

        state.Id.ShouldBe(stream);
        state.Version.ShouldBe(2);
    }

    [Fact]
    public async Task should_not_append_events_when_unexpected_max_version()
    {
        var joined = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        var stream = theSession.Events.StartStream<Quest>(joined).Id;
        await theSession.SaveChangesAsync();

        theSession.Events.Append(stream, 2, departed);

        using (var session = theStore.LightweightSession())
        {
            var joined3 = new MembersJoined { Members = new[] { "Egwene" } };
            var departed3 = new MembersDeparted { Members = new[] { "Perrin" } };

            session.Events.Append(stream, joined3, departed3);
            await session.SaveChangesAsync();
        }

        await Should.ThrowAsync<EventStreamUnexpectedMaxEventIdException>(() => theSession.SaveChangesAsync());

        using (var session = theStore.LightweightSession())
        {
            var state = await session.Events.FetchStreamStateAsync(stream);

            state.Id.ShouldBe(stream);
            state.Version.ShouldBe(3);
        }
    }

    [Fact]
    public async Task should_check_max_event_id_on_append_with_string_identifier()
    {
        UseStreamIdentity(StreamIdentity.AsString);

        var joined = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        var stream = "First";

        theSession.Events.Append(stream, joined);
        theSession.Events.Append(stream, 2, departed);

        await theSession.SaveChangesAsync();

        var state = theSession.Events.FetchStreamState(stream);

        state.Key.ShouldBe(stream);
        state.Version.ShouldBe(2);
    }

    [Fact]
    public async Task should_not_append_events_when_unexpected_max_version_with_string_identifier()
    {
        UseStreamIdentity(StreamIdentity.AsString);

        var joined = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        var stream = "Another";
        theSession.Events.Append(stream, joined);
        await theSession.SaveChangesAsync();

        theSession.Events.Append(stream, 2, departed);

        using (var session = theStore.LightweightSession())
        {
            var joined3 = new MembersJoined { Members = new[] { "Egwene" } };
            var departed3 = new MembersDeparted { Members = new[] { "Perrin" } };

            session.Events.Append(stream, joined3, departed3);
            await session.SaveChangesAsync();
        }

        await Should.ThrowAsync<EventStreamUnexpectedMaxEventIdException>(() => theSession.SaveChangesAsync());

        using (var session = theStore.LightweightSession())
        {
            var state = await session.Events.FetchStreamStateAsync(stream);

            state.Key.ShouldBe(stream);
            state.Version.ShouldBe(3);
        }
    }

    [Fact]
    public async Task should_check_max_event_id_on_append_to_empty_stream()
    {
        var joined = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };

        var stream = Guid.NewGuid();

        // This should fail because it's expecting the version on the server to exist
        // and be at 5 at the end of this
        theSession.Events.Append(stream, 5, joined);

        theSession.Logger = new TestOutputMartenLogger(_output);

        await Assert.ThrowsAsync<EventStreamUnexpectedMaxEventIdException>(async () => await theSession.SaveChangesAsync());
    }

    [Fact]
    public async Task happy_path_on_append_to_empty_stream()
    {
        var joined = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };

        var stream = Guid.NewGuid();

        // This should succeed because, yup, it puts you at exactly 1.
        // This really should be done w/ StartStream(), but oh well
        theSession.Events.Append(stream, 1, joined);

        await theSession.SaveChangesAsync();
    }

    [Fact]
    public void should_assert_that_the_expected_version_would_be_impossible()
    {
        var joined = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };

        var stream = Guid.NewGuid();

        Should.Throw<ArgumentOutOfRangeException>(() =>
            theSession.Events.Append(stream, 1, joined, new MonsterSlayed { Name = "Gorgon" }));

    }

    public asserting_on_expected_event_version_on_append(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        _output = output;
    }
}
