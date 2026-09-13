using JasperFx;
using Marten.PgVector;
using Marten.Testing.Harness;
using Pgvector;
using Shouldly;
using Xunit;
using System.Threading.Tasks;

namespace Marten.PgVector.Tests.SingleTenancy;

/// <summary>
///     The JSONB path <see cref="PgVectorExtensions.VectorSearchAsync{T}" /> reads the embedding from has
///     to be the one the serializer actually wrote.
/// </summary>
/// <remarks>
///     <para>
///         Every other test in this suite leaves the store on Marten's default casing, which preserves the
///         CLR member name — so a search built from <c>member.Name</c> lands on the right key by accident
///         and the suite is green either way. Under <c>Casing.CamelCase</c> the stored key is
///         <c>embedding</c> while <c>member.Name</c> is <c>Embedding</c>, <c>data-&gt;&gt;'Embedding'</c>
///         is SQL NULL for every row, the <c>IS NOT NULL</c> clause filters all of them out, and the
///         search returns an EMPTY LIST rather than throwing. A silent wrong answer, and the reason this
///         file exists at all.
///     </para>
/// </remarks>
[Collection("Marten.PgVector")]
public class vector_search_casing : IAsyncLifetime
{
    private DocumentStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "pgvector_casing_tests";
            opts.AutoCreateSchemaObjects = AutoCreate.All;

            opts.UseSystemTextJsonForSerialization(casing: Casing.CamelCase);

            opts.UsePgVector();
            opts.RegisterDocumentType<CasedProduct>();
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
    public async Task the_search_reads_the_key_the_serializer_wrote()
    {
        var near = new CasedProduct { Id = Guid.NewGuid(), Name = "Near", Embedding = [1.0f, 0.0f, 0.0f] };
        var far = new CasedProduct { Id = Guid.NewGuid(), Name = "Far", Embedding = [0.0f, 0.0f, 1.0f] };

        await using (var session = _store.LightweightSession())
        {
            session.Store(near, far);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var query = _store.QuerySession();

        var results = await query.VectorSearchAsync<CasedProduct>(
            x => x.Embedding,
            new Vector(new[] { 1.0f, 0.0f, 0.0f }),
            limit: 10,
            distance: DistanceFunction.L2);

        // Without the fix this is an empty list: every row is filtered out by the IS NOT NULL clause
        // because the path is the CLR member name and the stored key is camelCase.
        results.Count.ShouldBe(2);
        results[0].Name.ShouldBe("Near");
    }
}

public class CasedProduct
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public float[]? Embedding { get; set; }
}
