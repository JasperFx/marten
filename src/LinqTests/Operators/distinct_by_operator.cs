using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace LinqTests.Operators;

public class distinct_by_operator
{
    // https://github.com/JasperFx/marten/issues/4565
    // DistinctBy() is not translated to SQL. Rather than the generic
    // "operator not supported" message, callers get an actionable exception
    // pointing at the in-memory workaround. The exception is thrown while the
    // LINQ expression is parsed (before any database round trip).
    [Fact]
    public async Task distinct_by_throws_actionable_exception()
    {
        await using var store = DocumentStore.For(ConnectionSource.ConnectionString);
        await using var session = store.QuerySession();

        var ex = await Should.ThrowAsync<BadLinqExpressionException>(async () =>
        {
            await session.Query<Target>()
                .Select(x => new { x.Number, x.String })
                .DistinctBy(x => x.Number)
                .ToListAsync();
        });

        ex.Message.ShouldContain("DistinctBy");
        ex.Message.ShouldContain("ToListAsync");
        ex.Message.ShouldContain("4565");
    }
}
