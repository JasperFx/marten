using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Operators;

// #5731: Queryable.Join, the .NET 10 LeftJoin / RightJoin, the .NET 11 FullJoin and the .NET 11
// tuple-returning overloads of all four are refused, each with the GroupJoin() rewrite that answers
// it, rather than with the generic "does not (yet) support Linq operator" message. The refusal happens
// while the expression is parsed, so none of these touch the database.
public class join_operators_are_refused: IntegrationContext
{
    private const string GroupJoinDocs = "https://martendb.io/documents/querying/linq/group-join.html";

    public join_operators_are_refused(DefaultStoreFixture fixture): base(fixture)
    {
    }

    private async Task<string> refusalFor<T>(IQueryable<T> query)
    {
        var ex = await Should.ThrowAsync<BadLinqExpressionException>(() => query.ToListAsync());
        return ex.Message;
    }

    [Fact]
    public async Task join_points_at_group_join_and_select_many()
    {
        var message = await refusalFor(theSession.Query<JoinCustomer>()
            .Join(theSession.Query<JoinOrder>(), c => c.Id, o => o.CustomerId,
                (c, o) => new { c.Name, o.Amount }));

        message.ShouldContain("Queryable.Join()");
        message.ShouldContain("GroupJoin() followed by SelectMany()");
        message.ShouldContain("INNER JOIN");
        message.ShouldNotContain("DefaultIfEmpty");
        message.ShouldNotContain("tuples");
        message.ShouldContain(GroupJoinDocs);
    }

    [Fact]
    public async Task left_join_points_at_default_if_empty()
    {
        var message = await refusalFor(theSession.Query<JoinCustomer>()
            .LeftJoin(theSession.Query<JoinOrder>(), c => c.Id, o => o.CustomerId,
                (c, o) => new { c.Name, Amount = (decimal?)o.Amount }));

        message.ShouldContain("Queryable.LeftJoin()");
        message.ShouldContain("SelectMany() over DefaultIfEmpty()");
        message.ShouldContain("LEFT JOIN");
        message.ShouldContain(GroupJoinDocs);
    }

    [Fact]
    public async Task right_join_points_at_swapping_the_sources()
    {
        var message = await refusalFor(theSession.Query<JoinCustomer>()
            .RightJoin(theSession.Query<JoinOrder>(), c => c.Id, o => o.CustomerId,
                (c, o) => new { Name = c.Name, o.Amount }));

        message.ShouldContain("Queryable.RightJoin()");
        message.ShouldContain("Swap the two sources");
        message.ShouldContain(GroupJoinDocs);
    }

    [Fact]
    public async Task a_join_after_a_where_is_still_refused_by_name()
    {
        // The Where is applied first in the chain, so the join is not the outermost call the
        // parser meets on the way in — it still has to be the join that is refused.
        var message = await refusalFor(theSession.Query<JoinCustomer>()
            .Where(c => c.City == "Seattle")
            .Join(theSession.Query<JoinOrder>(), c => c.Id, o => o.CustomerId,
                (c, o) => new { c.Name, o.Amount })
            .Where(x => x.Amount > 10));

        message.ShouldContain("Queryable.Join()");
    }

#if NET11_0_OR_GREATER
    [Fact]
    public async Task full_join_says_there_is_no_translation()
    {
        var message = await refusalFor(theSession.Query<JoinCustomer>()
            .FullJoin(theSession.Query<JoinOrder>(), c => c.Id, o => o.CustomerId,
                (c, o) => new { Name = c.Name, Amount = (decimal?)o.Amount }));

        message.ShouldContain("Queryable.FullJoin()");
        message.ShouldContain("no FULL OUTER JOIN translation");
        message.ShouldContain("QueryAsync<T>(sql)");
        message.ShouldNotContain("tuples");
    }

    [Fact]
    public async Task the_tuple_join_overloads_name_the_result_selector()
    {
        var join = await refusalFor(theSession.Query<JoinCustomer>()
            .Join(theSession.Query<JoinOrder>(), c => c.Id, o => o.CustomerId));
        join.ShouldContain("Queryable.Join()");
        join.ShouldContain("GroupJoin() followed by SelectMany()");
        join.ShouldContain("returns (outer, inner) tuples");

        var left = await refusalFor(theSession.Query<JoinCustomer>()
            .LeftJoin(theSession.Query<JoinOrder>(), c => c.Id, o => o.CustomerId));
        left.ShouldContain("Queryable.LeftJoin()");
        left.ShouldContain("returns (outer, inner) tuples");

        var right = await refusalFor(theSession.Query<JoinCustomer>()
            .RightJoin(theSession.Query<JoinOrder>(), c => c.Id, o => o.CustomerId));
        right.ShouldContain("Queryable.RightJoin()");
        right.ShouldContain("returns (outer, inner) tuples");

        var full = await refusalFor(theSession.Query<JoinCustomer>()
            .FullJoin(theSession.Query<JoinOrder>(), c => c.Id, o => o.CustomerId));
        full.ShouldContain("Queryable.FullJoin()");
        full.ShouldContain("returns (outer, inner) tuples");
    }
#endif
}
