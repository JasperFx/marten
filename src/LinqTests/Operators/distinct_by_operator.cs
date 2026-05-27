using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace LinqTests.Operators;

// https://github.com/JasperFx/marten/issues/4565
// DistinctBy(keySelector) translates to PostgreSQL `SELECT DISTINCT ON (key) ...`.
public class distinct_by_operator
{
    private static IDocumentStore BuildStore(string schema)
    {
        return DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = schema;
        });
    }

    [Fact]
    public async Task distinct_by_a_projected_member()
    {
        await using var store = BuildStore("distinct_by_projected");
        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        await using (var session = store.LightweightSession())
        {
            session.Store(new Target { Number = 1, String = "a" });
            session.Store(new Target { Number = 1, String = "b" });
            session.Store(new Target { Number = 2, String = "c" });
            session.Store(new Target { Number = 2, String = "d" });
            session.Store(new Target { Number = 3, String = "e" });
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();

        var results = await query.Query<Target>()
            .Select(x => new { x.Number, x.String })
            .DistinctBy(x => x.Number)
            .ToListAsync();

        // One row per distinct Number; which String survives per group is arbitrary.
        results.Count.ShouldBe(3);
        results.Select(x => x.Number).OrderBy(x => x).ShouldHaveTheSameElementsAs(1, 2, 3);
    }

    [Fact]
    public async Task distinct_by_combined_with_where()
    {
        await using var store = BuildStore("distinct_by_where");
        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        await using (var session = store.LightweightSession())
        {
            session.Store(new Target { Number = 1, Flag = true });
            session.Store(new Target { Number = 1, Flag = true });
            session.Store(new Target { Number = 2, Flag = true });
            session.Store(new Target { Number = 3, Flag = false });
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();

        var results = await query.Query<Target>()
            .Where(x => x.Flag)
            .Select(x => new { x.Number, x.Flag })
            .DistinctBy(x => x.Number)
            .ToListAsync();

        results.Count.ShouldBe(2);
        results.Select(x => x.Number).OrderBy(x => x).ShouldHaveTheSameElementsAs(1, 2);
    }

    [Fact]
    public async Task distinct_by_without_a_select_throws_actionable_exception()
    {
        await using var store = BuildStore("distinct_by_no_select");
        await using var query = store.QuerySession();

        var ex = await Should.ThrowAsync<BadLinqExpressionException>(async () =>
        {
            await query.Query<Target>()
                .DistinctBy(x => x.Number)
                .ToListAsync();
        });

        ex.Message.ShouldContain("Select");
        ex.Message.ShouldContain("4565");
    }
}
