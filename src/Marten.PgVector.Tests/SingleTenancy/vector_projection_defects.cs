using JasperFx;
using JasperFx.Events.Projections;
using Marten.PgVector;
using Marten.PgVector.Projection;
using Marten.PgVector.Tests.Helpers;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using System.Threading.Tasks;
using JasperFx.Events.Vectors;
using Neutral = JasperFx.Events.Vectors;

namespace Marten.PgVector.Tests.SingleTenancy;

#region Events keyed on something other than the stream

public record ArticleWritten(Guid ArticleId, string Body);

public record ArticleRetracted(Guid ArticleId);

public record ArticleWithABadSelector(Guid ArticleId);

#endregion

/// <summary>
///     A projection whose rows are keyed on a member of the event rather than on the stream.
/// </summary>
/// <remarks>
///     <para>
///         <c>vector_projection_tests</c> uses one Guid as BOTH the stream id and the payload id, which is
///         the reason its delete test passes over a delete path that only ever looks at the stream id.
///         Here the two are deliberately different, which is the only arrangement that can tell them
///         apart.
///     </para>
/// </remarks>
public class ArticleSearchProjection: VectorProjection
{
    public ArticleSearchProjection(Neutral.IEmbeddingProvider provider)
        : base("article_search_vectors", provider)
    {
    }

    protected override void Configure(VectorProjectionMapping map)
    {
        map.Map<ArticleWritten>(e => e.Body, e => e.ArticleId);
        map.Map<ArticleWithABadSelector>(
            _ => throw new InvalidOperationException("the content selector is broken"),
            e => e.ArticleId);
        map.Delete<ArticleRetracted>(e => e.ArticleId);
    }
}

/// <summary>
///     Keys its rows on the article but deletes by the stream, which addresses a row that was never
///     written. Refused while the store is being built.
/// </summary>
public class MismatchedDeleteProjection: VectorProjection
{
    public MismatchedDeleteProjection(Neutral.IEmbeddingProvider provider)
        : base("mismatched_vectors", provider)
    {
    }

    protected override void Configure(VectorProjectionMapping map)
    {
        map.Map<ArticleWritten>(e => e.Body, e => e.ArticleId);
        map.Delete<ArticleRetracted>();
    }
}

[Collection("Marten.PgVector")]
public class vector_projection_defects: IAsyncLifetime
{
    private FakeEmbeddingProvider _embedder = null!;
    private DocumentStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        _embedder = new FakeEmbeddingProvider(3);
        var projection = new ArticleSearchProjection(_embedder);

        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "pgvector_defect_tests";
            opts.AutoCreateSchemaObjects = AutoCreate.All;

            opts.UsePgVector();
            opts.Projections.Add(projection, ProjectionLifecycle.Inline);
            opts.Storage.ExtendedSchemaObjects.Add(projection.BuildTable("pgvector_defect_tests"));

            opts.Events.AddEventType<ArticleWritten>();
            opts.Events.AddEventType<ArticleRetracted>();
            opts.Events.AddEventType<ArticleWithABadSelector>();
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    public ValueTask DisposeAsync()
    {
        _store?.Dispose();
        return default;
    }

    [Fact]
    public async Task a_delete_removes_the_row_the_mapping_keyed()
    {
        // The stream and the article are deliberately different identities.
        var streamId = Guid.NewGuid();
        var articleId = Guid.NewGuid();

        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream(streamId, new ArticleWritten(articleId, "the body of the article"));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var session = _store.LightweightSession())
        {
            var written = await session.VectorProjectionSearchAsync("article_search_vectors",
                _embedder.GenerateVector("the body of the article"), 10, Neutral.DistanceFunction.L2);
            written.Single().Id.ShouldBe(articleId);

            session.Events.Append(streamId, new ArticleRetracted(articleId));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var query = _store.QuerySession();
        var remaining = await query.VectorProjectionSearchAsync("article_search_vectors",
            _embedder.GenerateVector("the body of the article"), 10, Neutral.DistanceFunction.L2);

        // Without the fix the delete runs against the STREAM id, matches no row, and the retracted
        // article stays in the index forever.
        remaining.ShouldBeEmpty();
    }

    [Fact]
    public void a_delete_that_cannot_address_the_mapped_row_is_refused_when_the_store_is_built()
    {
        // The refusal is what makes the fix above safe to rely on: an optional selector that silently
        // fell back to the stream id would leave exactly the defect this file exists for, just opt-in.
        var ex = Should.Throw<InvalidOperationException>(() => new MismatchedDeleteProjection(_embedder));

        ex.Message.ShouldContain("ArticleWritten");
        ex.Message.ShouldContain("ArticleRetracted");
        ex.Message.ShouldContain("map.Delete<ArticleRetracted>(e => e.SomeId)");
    }

    [Fact]
    public async Task a_content_selector_that_throws_is_not_swallowed()
    {
        var streamId = Guid.NewGuid();

        await using var session = _store.LightweightSession();
        session.Events.StartStream(streamId, new ArticleWithABadSelector(Guid.NewGuid()));

        // Without the fix the exception is caught, the content reads as null, the document is dropped
        // from the index, and the commit succeeds — so a broken selector is indistinguishable from an
        // event the projection does not index.
        var ex = await Should.ThrowAsync<Exception>(() =>
            session.SaveChangesAsync(TestContext.Current.CancellationToken));

        ex.ToString().ShouldContain("the content selector is broken");
    }
}

/// <summary>
///     marten#5422: a page that both writes and retracts the same id has to end in the state its
///     events describe, whatever order the two arrive in.
/// </summary>
/// <remarks>
///     <para>
///         <b>These pass on 9.37 and are regression guards rather than a fix</b>, and the reason is
///         worth recording. The defect was real when it was reported: the projection sorted a page into
///         a deletions list and an extractions list and ran all of the first before all of the second,
///         so a write-then-retract page deleted nothing (the row did not exist yet) and then inserted
///         the retracted content. Adopting the shared core in <c>1a246a8b2</c> replaced that with
///         <c>JasperFx.Events.Vectors.VectorEmbeddingPlan.Build</c>, which folds the page in EVENT
///         ORDER into a per-id final state — so the defect went away as a side effect of a change made
///         for other reasons, and nothing in Marten pinned it.
///     </para>
///     <para>
///         ⚠️ The async daemon is where this mattered most: a write and its retraction land in one page
///         routinely during catch-up, and on a REBUILD every retracted document whose write and
///         retraction share a page came back.
///     </para>
/// </remarks>
[Collection("Marten.PgVector")]
public class vector_projection_page_folding: IAsyncLifetime
{
    private FakeEmbeddingProvider _embedder = null!;
    private DocumentStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        _embedder = new FakeEmbeddingProvider(3);
        var projection = new ArticleSearchProjection(_embedder);

        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "pgvector_page_folding";
            opts.AutoCreateSchemaObjects = AutoCreate.All;

            opts.UsePgVector();
            opts.Projections.Add(projection, ProjectionLifecycle.Inline);
            opts.Storage.ExtendedSchemaObjects.Add(projection.BuildTable("pgvector_page_folding"));

            opts.Events.AddEventType<ArticleWritten>();
            opts.Events.AddEventType<ArticleRetracted>();
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    public ValueTask DisposeAsync()
    {
        _store?.Dispose();
        return default;
    }

    private async Task<IReadOnlyList<VectorSearchResult>> searchAsync(string text)
    {
        await using var query = _store.QuerySession();
        return await query.VectorProjectionSearchAsync(
            "article_search_vectors", _embedder.GenerateVector(text), 10, Neutral.DistanceFunction.L2);
    }

    /// <summary>A write and a retraction of the same id in ONE page ends deleted.</summary>
    [Fact]
    public async Task written_then_retracted_in_one_page_leaves_no_row()
    {
        var streamId = Guid.NewGuid();
        var articleId = Guid.NewGuid();

        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream(streamId,
                new ArticleWritten(articleId, "a draft nobody should be able to find"),
                new ArticleRetracted(articleId));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        (await searchAsync("a draft nobody should be able to find")).ShouldBeEmpty();
    }

    /// <summary>
    ///     And the other way round: a retraction followed by a write in one page ends WRITTEN, because
    ///     that is the state the events describe.
    /// </summary>
    [Fact]
    public async Task retracted_then_written_in_one_page_keeps_the_new_content()
    {
        var streamId = Guid.NewGuid();
        var articleId = Guid.NewGuid();

        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream(streamId, new ArticleWritten(articleId, "the first draft"));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var session = _store.LightweightSession())
        {
            session.Events.Append(streamId,
                new ArticleRetracted(articleId),
                new ArticleWritten(articleId, "the reinstated draft"));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var hits = await searchAsync("the reinstated draft");
        hits.Single().Id.ShouldBe(articleId);
        hits.Single().ContentText.ShouldBe("the reinstated draft");
    }

    /// <summary>
    ///     Two writes for one id in one page cost ONE model call, for the last content.
    /// </summary>
    /// <remarks>
    ///     A page is applied as one unit, so embedding the intermediate state would spend a call at the
    ///     provider's meter on text no reader could ever have observed. This is the fact that would
    ///     catch a future "fix" that restored per-event processing.
    /// </remarks>
    [Fact]
    public async Task two_writes_for_one_id_in_one_page_cost_one_model_call()
    {
        var streamId = Guid.NewGuid();
        var articleId = Guid.NewGuid();

        _embedder.RequestedTexts.Clear();

        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream(streamId,
                new ArticleWritten(articleId, "the superseded text"),
                new ArticleWritten(articleId, "the final text"));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // The fact. One call, for the LAST content — the superseded text is never embedded.
        _embedder.RequestedTexts.ShouldBe(["the final text"]);

        // ⚠️ Asserted as "one row holding the final text", NOT as "a search for the superseded text
        // finds nothing". A vector search returns the top-k by distance with no similarity threshold,
        // so over a one-row table it returns that row whatever you search for — an emptiness assertion
        // there would be about the corpus size, not about the content.
        var rows = await searchAsync("the final text");
        rows.Single().Id.ShouldBe(articleId);
        rows.Single().ContentText.ShouldBe("the final text");
    }
}
