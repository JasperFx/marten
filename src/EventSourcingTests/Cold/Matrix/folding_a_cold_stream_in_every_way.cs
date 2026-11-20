using System.Threading.Tasks;
using Xunit;

namespace EventSourcingTests.Cold.Matrix;

// Every fetch, for every lifecycle, identity, write cache setting, session type and cold shape, folds the whole
// stream: the cold part, then the warm events, each once. A writing fetch then appends and commits. Each class is
// one store: the settings below are store-wide.

public class folding_a_cold_stream_in_every_way_live_and_inline: IClassFixture<LiveAndInlineColdMatrixFixture>
{
    private readonly LiveAndInlineColdMatrixFixture _fixture;

    public folding_a_cold_stream_in_every_way_live_and_inline(LiveAndInlineColdMatrixFixture fixture)
    {
        _fixture = fixture;
    }

    public static TheoryData<ColdIdentity, ColdLifecycle, ColdDocument, ColdShape, ColdFetch, bool, ColdSession> Cases =>
        ColdMatrix.Cases([ColdLifecycle.Live, ColdLifecycle.Inline],
            [ColdSession.Lightweight, ColdSession.IdentityMap, ColdSession.DirtyTracked]);

    [Theory]
    [MemberData(nameof(Cases))]
    public Task folds_the_whole_stream(ColdIdentity identity, ColdLifecycle lifecycle, ColdDocument document,
        ColdShape shape, ColdFetch fetch, bool cached, ColdSession session)
        => ColdMatrix.RunAsync(_fixture, identity, lifecycle, document, shape, fetch, cached, session);

    public static TheoryData<ColdIdentity, ColdLifecycle, ColdFetch, bool> MoveBeforeTheCommitCases =>
        ColdMatrix.MoveBeforeTheCommitCases([ColdLifecycle.Live, ColdLifecycle.Inline]);

    [Theory]
    [MemberData(nameof(MoveBeforeTheCommitCases))]
    public Task a_move_between_the_fetch_and_the_commit(ColdIdentity identity, ColdLifecycle lifecycle,
        ColdFetch fetch, bool cached)
        => ColdMatrix.RunWithAMoveBeforeTheCommitAsync(_fixture, identity, lifecycle, fetch, cached);
}

public class folding_a_cold_stream_in_every_way_with_the_identity_map_for_aggregates
    : IClassFixture<LiveAndInlineWithIdentityMapColdMatrixFixture>
{
    private readonly LiveAndInlineWithIdentityMapColdMatrixFixture _fixture;

    public folding_a_cold_stream_in_every_way_with_the_identity_map_for_aggregates(
        LiveAndInlineWithIdentityMapColdMatrixFixture fixture)
    {
        _fixture = fixture;
    }

    public static TheoryData<ColdIdentity, ColdLifecycle, ColdDocument, ColdShape, ColdFetch, bool, ColdSession> Cases =>
        ColdMatrix.Cases([ColdLifecycle.Live, ColdLifecycle.Inline],
            [ColdSession.Lightweight, ColdSession.IdentityMap, ColdSession.DirtyTracked]);

    [Theory]
    [MemberData(nameof(Cases))]
    public Task folds_the_whole_stream(ColdIdentity identity, ColdLifecycle lifecycle, ColdDocument document,
        ColdShape shape, ColdFetch fetch, bool cached, ColdSession session)
        => ColdMatrix.RunAsync(_fixture, identity, lifecycle, document, shape, fetch, cached, session);

    public static TheoryData<ColdIdentity, ColdLifecycle, ColdFetch, bool> MoveBeforeTheCommitCases =>
        ColdMatrix.MoveBeforeTheCommitCases([ColdLifecycle.Live, ColdLifecycle.Inline]);

    [Theory]
    [MemberData(nameof(MoveBeforeTheCommitCases))]
    public Task a_move_between_the_fetch_and_the_commit(ColdIdentity identity, ColdLifecycle lifecycle,
        ColdFetch fetch, bool cached)
        => ColdMatrix.RunWithAMoveBeforeTheCommitAsync(_fixture, identity, lifecycle, fetch, cached);
}

public class folding_a_cold_stream_in_every_way_under_conjoined_tenancy
    : IClassFixture<LiveAndInlineConjoinedColdMatrixFixture>
{
    private readonly LiveAndInlineConjoinedColdMatrixFixture _fixture;

    public folding_a_cold_stream_in_every_way_under_conjoined_tenancy(LiveAndInlineConjoinedColdMatrixFixture fixture)
    {
        _fixture = fixture;
    }

    public static TheoryData<ColdIdentity, ColdLifecycle, ColdDocument, ColdShape, ColdFetch, bool, ColdSession> Cases =>
        ColdMatrix.Cases([ColdLifecycle.Live, ColdLifecycle.Inline], [ColdSession.Lightweight]);

    [Theory]
    [MemberData(nameof(Cases))]
    public Task folds_the_whole_stream(ColdIdentity identity, ColdLifecycle lifecycle, ColdDocument document,
        ColdShape shape, ColdFetch fetch, bool cached, ColdSession session)
        => ColdMatrix.RunAsync(_fixture, identity, lifecycle, document, shape, fetch, cached, session);

    public static TheoryData<ColdIdentity, ColdLifecycle, ColdFetch, bool> MoveBeforeTheCommitCases =>
        ColdMatrix.MoveBeforeTheCommitCases([ColdLifecycle.Live, ColdLifecycle.Inline]);

    [Theory]
    [MemberData(nameof(MoveBeforeTheCommitCases))]
    public Task a_move_between_the_fetch_and_the_commit(ColdIdentity identity, ColdLifecycle lifecycle,
        ColdFetch fetch, bool cached)
        => ColdMatrix.RunWithAMoveBeforeTheCommitAsync(_fixture, identity, lifecycle, fetch, cached);
}

public class folding_a_cold_stream_in_every_way_async: IClassFixture<AsyncColdMatrixFixture>
{
    private readonly AsyncColdMatrixFixture _fixture;

    public folding_a_cold_stream_in_every_way_async(AsyncColdMatrixFixture fixture)
    {
        _fixture = fixture;
    }

    public static TheoryData<ColdIdentity, ColdLifecycle, ColdDocument, ColdShape, ColdFetch, bool, ColdSession> Cases =>
        ColdMatrix.Cases([ColdLifecycle.Async],
            [ColdSession.Lightweight, ColdSession.IdentityMap, ColdSession.DirtyTracked]);

    [Theory]
    [MemberData(nameof(Cases))]
    public Task folds_the_whole_stream(ColdIdentity identity, ColdLifecycle lifecycle, ColdDocument document,
        ColdShape shape, ColdFetch fetch, bool cached, ColdSession session)
        => ColdMatrix.RunAsync(_fixture, identity, lifecycle, document, shape, fetch, cached, session);

    public static TheoryData<ColdIdentity, ColdLifecycle, ColdFetch, bool> MoveBeforeTheCommitCases =>
        ColdMatrix.MoveBeforeTheCommitCases([ColdLifecycle.Async]);

    [Theory]
    [MemberData(nameof(MoveBeforeTheCommitCases))]
    public Task a_move_between_the_fetch_and_the_commit(ColdIdentity identity, ColdLifecycle lifecycle,
        ColdFetch fetch, bool cached)
        => ColdMatrix.RunWithAMoveBeforeTheCommitAsync(_fixture, identity, lifecycle, fetch, cached);
}

public class folding_a_cold_stream_in_every_way_async_with_the_identity_map_for_aggregates
    : IClassFixture<AsyncWithIdentityMapColdMatrixFixture>
{
    private readonly AsyncWithIdentityMapColdMatrixFixture _fixture;

    public folding_a_cold_stream_in_every_way_async_with_the_identity_map_for_aggregates(
        AsyncWithIdentityMapColdMatrixFixture fixture)
    {
        _fixture = fixture;
    }

    public static TheoryData<ColdIdentity, ColdLifecycle, ColdDocument, ColdShape, ColdFetch, bool, ColdSession> Cases =>
        ColdMatrix.Cases([ColdLifecycle.Async], [ColdSession.Lightweight]);

    [Theory]
    [MemberData(nameof(Cases))]
    public Task folds_the_whole_stream(ColdIdentity identity, ColdLifecycle lifecycle, ColdDocument document,
        ColdShape shape, ColdFetch fetch, bool cached, ColdSession session)
        => ColdMatrix.RunAsync(_fixture, identity, lifecycle, document, shape, fetch, cached, session);
}
