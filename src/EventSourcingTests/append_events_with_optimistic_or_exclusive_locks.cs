using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Exceptions;
using Marten.Services;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

/// <summary>
/// AppendOptimistic/AppendExclusive behavior across every EventAppendMode and
/// both stream identity styles. Replaces the old default-mode-only classes here
/// plus their Quick-mode fork in
/// QuickAppend/quick_append_events_with_optimistic_or_exclusive_locks.cs.
/// </summary>
public class AppendLocksFixture: MultiStoreFixture
{
    public static readonly IReadOnlyDictionary<string, (EventAppendMode Mode, StreamIdentity Identity)> Cases =
        new Dictionary<string, (EventAppendMode, StreamIdentity)>
        {
            { "rich_guid", (EventAppendMode.Rich, StreamIdentity.AsGuid) },
            { "quick_guid", (EventAppendMode.Quick, StreamIdentity.AsGuid) },
            { "qwst_guid", (EventAppendMode.QuickWithServerTimestamps, StreamIdentity.AsGuid) },
            { "rich_string", (EventAppendMode.Rich, StreamIdentity.AsString) },
            { "quick_string", (EventAppendMode.Quick, StreamIdentity.AsString) },
            { "qwst_string", (EventAppendMode.QuickWithServerTimestamps, StreamIdentity.AsString) }
        };

    public AppendLocksFixture(): base("eslocks")
    {
        foreach (var pair in Cases)
        {
            var (mode, identity) = pair.Value;
            Profile(pair.Key, opts =>
            {
                opts.Events.AppendMode = mode;
                opts.Events.StreamIdentity = identity;
            });
        }
    }

    public static IEnumerable<object[]> ProfilesFor(StreamIdentity identity)
    {
        foreach (var pair in Cases)
        {
            if (pair.Value.Identity == identity)
            {
                yield return new object[] { pair.Key };
            }
        }
    }
}

public class append_events_optimistic_or_exclusive_with_guid_identity: IClassFixture<AppendLocksFixture>
{
    private readonly AppendLocksFixture _fixture;

    public append_events_optimistic_or_exclusive_with_guid_identity(AppendLocksFixture fixture)
    {
        _fixture = fixture;
    }

    public static IEnumerable<object[]> Profiles()
    {
        return AppendLocksFixture.ProfilesFor(StreamIdentity.AsGuid);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task append_optimistic_sad_path_because_the_stream_does_not_already_exist(string profile)
    {
        var store = _fixture.StoreFor(profile);
        await using var session = store.LightweightSession();

        var streamId = Guid.NewGuid();
        var ex = await Should.ThrowAsync<NonExistentStreamException>(async () =>
        {
            await session.Events.AppendOptimistic(streamId, new AEvent(), new BEvent());
        });

        ex.Id.ShouldBe(streamId);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task append_optimistic_happy_path(string profile)
    {
        var store = _fixture.StoreFor(profile);
        await using var session = store.LightweightSession();

        var streamId = Guid.NewGuid();
        session.Events.StartStream(streamId, new AEvent(), new BEvent());
        await session.SaveChangesAsync();

        await session.Events.AppendOptimistic(streamId, new CEvent(), new BEvent());
        await session.SaveChangesAsync();

        var state = await session.Events.FetchStreamStateAsync(streamId);
        state.Version.ShouldBe(4);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task append_optimistic_sad_path_with_concurrency_issue(string profile)
    {
        var store = _fixture.StoreFor(profile);

        var streamId = Guid.NewGuid();
        await using var session1 = store.LightweightSession(new SessionOptions { Timeout = 1 });

        session1.Events.StartStream(streamId, new AEvent(), new BEvent());
        await session1.SaveChangesAsync();

        // Fetch the expected version
        await session1.Events.AppendOptimistic(streamId, new CEvent(), new BEvent());

        await using (var session = store.LightweightSession(new SessionOptions { Timeout = 1 }))
        {
            session.Events.Append(streamId, new DEvent());
            await session.SaveChangesAsync();
        }

        // Should fail a concurrency check
        await Should.ThrowAsync<EventStreamUnexpectedMaxEventIdException>(async () =>
        {
            await session1.SaveChangesAsync();
        });
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task append_exclusive_sad_path_because_the_stream_does_not_already_exist(string profile)
    {
        var store = _fixture.StoreFor(profile);
        await using var session = store.LightweightSession();

        var streamId = Guid.NewGuid();
        var ex = await Should.ThrowAsync<NonExistentStreamException>(async () =>
        {
            await session.Events.AppendExclusive(streamId, new AEvent(), new BEvent());
        });

        ex.Id.ShouldBe(streamId);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task append_exclusive_happy_path(string profile)
    {
        var store = _fixture.StoreFor(profile);
        await using var session = store.LightweightSession();

        var streamId = Guid.NewGuid();
        session.Events.StartStream(streamId, new AEvent(), new BEvent());
        await session.SaveChangesAsync();

        await session.Events.AppendExclusive(streamId, new CEvent(), new BEvent());
        await session.SaveChangesAsync();

        var state = await session.Events.FetchStreamStateAsync(streamId);
        state.Version.ShouldBe(4);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task append_exclusive_sad_path_with_concurrency_issue(string profile)
    {
        var store = _fixture.StoreFor(profile);
        await using var lockingSession = store.LightweightSession();

        var streamId = Guid.NewGuid();
        lockingSession.Events.StartStream(streamId, new AEvent(), new BEvent());
        await lockingSession.SaveChangesAsync();

        // Fetch the expected version
        await lockingSession.Events.AppendExclusive(streamId, new CEvent(), new BEvent());

        await using (var session = store.LightweightSession(new SessionOptions { Timeout = 1 }))
        {
            session.Events.Append(streamId, new DEvent());
            var ex = await Should.ThrowAsync<MartenCommandException>(async () =>
            {
                await session.SaveChangesAsync();
            });

            ex.Message.ShouldContain(MartenCommandException.MaybeLockedRowsMessage);
        }
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task append_exclusive_sad_path_with_concurrency_issue_2(string profile)
    {
        var store = _fixture.StoreFor(profile);
        await using var lockingSession = store.LightweightSession();

        var streamId = Guid.NewGuid();
        lockingSession.Events.StartStream(streamId, new AEvent(), new BEvent());
        await lockingSession.SaveChangesAsync();

        // Fetch the expected version
        await lockingSession.Events.AppendExclusive(streamId, new CEvent(), new BEvent());

        await using (var session = store.LightweightSession(new SessionOptions { Timeout = 1 }))
        {
            await Should.ThrowAsync<StreamLockedException>(async () =>
            {
                await session.Events.AppendExclusive(streamId, new DEvent());
            });
        }
    }
}

public class append_events_optimistic_or_exclusive_with_string_identity: IClassFixture<AppendLocksFixture>
{
    private readonly AppendLocksFixture _fixture;

    public append_events_optimistic_or_exclusive_with_string_identity(AppendLocksFixture fixture)
    {
        _fixture = fixture;
    }

    public static IEnumerable<object[]> Profiles()
    {
        return AppendLocksFixture.ProfilesFor(StreamIdentity.AsString);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task append_optimistic_sad_path_because_the_stream_does_not_already_exist(string profile)
    {
        var store = _fixture.StoreFor(profile);
        await using var session = store.LightweightSession();

        var streamId = Guid.NewGuid().ToString();
        var ex = await Should.ThrowAsync<NonExistentStreamException>(async () =>
        {
            await session.Events.AppendOptimistic(streamId, new AEvent(), new BEvent());
        });

        ex.Id.ShouldBe(streamId);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task append_optimistic_happy_path(string profile)
    {
        var store = _fixture.StoreFor(profile);
        await using var session = store.LightweightSession();

        var streamId = Guid.NewGuid().ToString();
        session.Events.StartStream(streamId, new AEvent(), new BEvent());
        await session.SaveChangesAsync();

        await session.Events.AppendOptimistic(streamId, new CEvent(), new BEvent());
        await session.SaveChangesAsync();

        var state = await session.Events.FetchStreamStateAsync(streamId);
        state.Version.ShouldBe(4);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task append_optimistic_sad_path_with_concurrency_issue(string profile)
    {
        var store = _fixture.StoreFor(profile);
        await using var session1 = store.LightweightSession();

        var streamId = Guid.NewGuid().ToString();
        session1.Events.StartStream(streamId, new AEvent(), new BEvent());
        await session1.SaveChangesAsync();

        // Fetch the expected version
        await session1.Events.AppendOptimistic(streamId, new CEvent(), new BEvent());

        await using (var session = store.LightweightSession())
        {
            session.Events.Append(streamId, new DEvent());
            await session.SaveChangesAsync();
        }

        // Should fail a concurrency check
        await Should.ThrowAsync<EventStreamUnexpectedMaxEventIdException>(async () =>
        {
            await session1.SaveChangesAsync();
        });
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task append_exclusive_sad_path_because_the_stream_does_not_already_exist(string profile)
    {
        var store = _fixture.StoreFor(profile);
        await using var session = store.LightweightSession();

        var streamId = Guid.NewGuid().ToString();
        var ex = await Should.ThrowAsync<NonExistentStreamException>(async () =>
        {
            await session.Events.AppendExclusive(streamId, new AEvent(), new BEvent());
        });

        ex.Id.ShouldBe(streamId);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task append_exclusive_happy_path(string profile)
    {
        var store = _fixture.StoreFor(profile);
        await using var session = store.LightweightSession();

        var streamId = Guid.NewGuid().ToString();
        session.Events.StartStream(streamId, new AEvent(), new BEvent());
        await session.SaveChangesAsync();

        await session.Events.AppendExclusive(streamId, new CEvent(), new BEvent());
        await session.SaveChangesAsync();

        var state = await session.Events.FetchStreamStateAsync(streamId);
        state.Version.ShouldBe(4);
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task append_exclusive_sad_path_with_concurrency_issue(string profile)
    {
        var store = _fixture.StoreFor(profile);
        await using var lockingSession = store.LightweightSession();

        var streamId = Guid.NewGuid().ToString();
        lockingSession.Events.StartStream(streamId, new AEvent(), new BEvent());
        await lockingSession.SaveChangesAsync();

        // Fetch the expected version
        await lockingSession.Events.AppendExclusive(streamId, new CEvent(), new BEvent());

        await using (var session = store.LightweightSession(new SessionOptions { Timeout = 1 }))
        {
            session.Events.Append(streamId, new DEvent());
            var ex = await Should.ThrowAsync<MartenCommandException>(async () =>
            {
                await session.SaveChangesAsync();
            });

            ex.Message.ShouldContain(MartenCommandException.MaybeLockedRowsMessage);
        }
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task append_exclusive_sad_path_with_concurrency_issue_2(string profile)
    {
        var store = _fixture.StoreFor(profile);
        await using var lockingSession = store.LightweightSession();

        var streamId = Guid.NewGuid().ToString();
        lockingSession.Events.StartStream(streamId, new AEvent(), new BEvent());
        await lockingSession.SaveChangesAsync();

        // Fetch the expected version
        await lockingSession.Events.AppendExclusive(streamId, new CEvent(), new BEvent());

        await using (var session = store.LightweightSession(new SessionOptions { Timeout = 1 }))
        {
            await Should.ThrowAsync<StreamLockedException>(async () =>
            {
                await session.Events.AppendExclusive(streamId, new DEvent());
            });
        }
    }
}
