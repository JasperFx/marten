using JasperFx;
using JasperFx.Events.Projections;
using Marten.PgVector;
using Marten.PgVector.Projection;
using Marten.PgVector.Tests.Helpers;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using System.Threading.Tasks;

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
    public ArticleSearchProjection(IEmbeddingProvider provider)
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
    public MismatchedDeleteProjection(IEmbeddingProvider provider)
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
                _embedder.GenerateVector("the body of the article"), 10, DistanceFunction.L2);
            written.Single().Id.ShouldBe(articleId);

            session.Events.Append(streamId, new ArticleRetracted(articleId));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var query = _store.QuerySession();
        var remaining = await query.VectorProjectionSearchAsync("article_search_vectors",
            _embedder.GenerateVector("the body of the article"), 10, DistanceFunction.L2);

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
