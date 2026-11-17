using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Operators;

// #5731 / #5910: Join, LeftJoin and RightJoin translate by being rewritten into GroupJoin() +
// SelectMany(), but only for the shapes that rewrite answers correctly. Everything else is refused with
// the reason rather than left to the GroupJoin path, which would drop what it does not understand and
// return plausible but wrong rows. FullJoin has no translation at all. Every refusal happens while the
// expression is parsed, so none of these touch the database.
public class join_operators_are_refused: IntegrationContext
{
    private const string GroupJoinDocs = "https://martendb.io/documents/querying/linq/group-join.html";

    public join_operators_are_refused(DefaultStoreFixture fixture): base(fixture)
    {
    }

    private async Task<string> refusalFor<T>(IQueryable<T> query)
    {
        var ex = await Should.ThrowAsync<BadLinqExpressionException>(() => query.ToListAsync());
        ex.Message.ShouldContain(GroupJoinDocs);
        return ex.Message;
    }

    [Fact]
    public async Task an_operator_other_than_where_before_the_join_is_refused()
    {
        // Take() first would join only the first rows, an ordering is not kept by a SQL join, and a
        // projection is not a document the GroupJoin path can read
        var take = await refusalFor(theSession.Query<JoinCustomer>()
            .Take(2)
            .Join(theSession.Query<JoinOrder>(), c => c.Id, o => o.CustomerId, (c, o) => new { c.Name, o.Amount }));
        take.ShouldContain("Queryable.Join()");
        take.ShouldContain("only Where() may come before it on the outer source");
        take.ShouldContain("Take()");

        var ordered = await refusalFor(theSession.Query<JoinCustomer>()
            .OrderBy(c => c.Name)
            .LeftJoin(theSession.Query<JoinOrder>(), c => c.Id, o => o.CustomerId,
                (c, o) => new { c.Name, Amount = (decimal?)o.Amount }));
        ordered.ShouldContain("Queryable.LeftJoin()");
        ordered.ShouldContain("OrderBy()");

        var projected = await refusalFor(theSession.Query<JoinCustomer>()
            .Select(c => new JoinCustomer { Id = c.Id, Name = c.Name })
            .Join(theSession.Query<JoinOrder>(), c => c.Id, o => o.CustomerId, (c, o) => new { c.Name, o.Amount }));
        projected.ShouldContain("Select()");
    }

    [Fact]
    public async Task an_inner_source_that_is_not_a_filtered_document_query_is_refused()
    {
        var paged = await refusalFor(theSession.Query<JoinCustomer>()
            .Join(theSession.Query<JoinOrder>().OrderBy(o => o.Amount).Take(1), c => c.Id, o => o.CustomerId,
                (c, o) => new { c.Name, o.Amount }));
        paged.ShouldContain("the inner source may only be session.Query<T>() filtered by Where()");
        paged.ShouldContain("Take()");

        var inMemory = new List<JoinOrder> { new() { Id = Guid.NewGuid(), CustomerId = Guid.NewGuid() } };
        var memory = await refusalFor(theSession.Query<JoinCustomer>()
            .Join(inMemory, c => c.Id, o => o.CustomerId, (c, o) => new { c.Name, o.Amount }));
        memory.ShouldContain("the inner source must be a Marten session.Query<T>()");
        memory.ShouldContain("in-memory collection");
    }

    [Fact]
    public async Task right_join_needs_both_sources_to_be_filtered_document_queries()
    {
        // The sources swap, so the outer one is the GroupJoin's inner source and has the same rules
        var message = await refusalFor(theSession.Query<JoinCustomer>()
            .Take(2)
            .RightJoin(theSession.Query<JoinOrder>(), c => c.Id, o => o.CustomerId, (c, o) => new { c.Name, o.Amount }));

        message.ShouldContain("Queryable.RightJoin()");
        message.ShouldContain("the outer source may only be session.Query<T>() filtered by Where()");
    }

    [Fact]
    public async Task a_composite_or_computed_key_is_refused()
    {
        var composite = await refusalFor(theSession.Query<JoinCustomer>()
            .Join(theSession.Query<JoinEmployee>(), c => new { c.Name, c.City }, e => new { e.Name, e.City },
                (c, e) => new { Customer = c.Name, Employee = e.Name }));
        composite.ShouldContain("composite key");

        var computed = await refusalFor(theSession.Query<JoinCustomer>()
            .Join(theSession.Query<JoinEmployee>(), c => c.City.ToUpper(), e => e.City,
                (c, e) => new { Customer = c.Name, Employee = e.Name }));
        computed.ShouldContain("the outer key selector must be a member of the document");
    }

    [Fact]
    public async Task the_comparer_overload_is_refused()
    {
        var message = await refusalFor(theSession.Query<JoinCustomer>()
            .Join(theSession.Query<JoinEmployee>(), c => c.City, e => e.City,
                (c, e) => new { Customer = c.Name, Employee = e.Name }, StringComparer.OrdinalIgnoreCase));

        message.ShouldContain("IEqualityComparer");
    }

    [Fact]
    public async Task a_join_of_a_join_is_refused()
    {
        var message = await refusalFor(theSession.Query<JoinCustomer>()
            .Join(theSession.Query<JoinOrder>(), c => c.Id, o => o.CustomerId, (c, o) => new { c.City, o.Amount })
            .Join(theSession.Query<JoinEmployee>(), x => x.City, e => e.City, (x, e) => new { x.Amount, e.Name }));

        message.ShouldContain("one of its sources is itself a join");

        var groupJoined = await refusalFor(theSession.Query<JoinCustomer>()
            .Join(theSession.Query<JoinOrder>(), c => c.Id, o => o.CustomerId, (c, o) => new { c.City, o.Amount })
            .GroupJoin(theSession.Query<JoinEmployee>(), x => x.City, e => e.City, (x, employees) => new { x, employees })
            .SelectMany(y => y.employees, (y, e) => new { y.x.Amount, e.Name }));

        groupJoined.ShouldContain("Queryable.GroupJoin()");
        groupJoined.ShouldContain("one of its sources is itself a join");
    }

    [Fact]
    public async Task a_where_on_the_inner_side_after_a_left_join_is_refused()
    {
        // Marten applies an inner-side filter to the inner rows before joining. For a left join that
        // would keep the customers without a matching order, with a null order, which the filter after
        // the join removes -- plausible rows, and wrong ones
        var left = await Should.ThrowAsync<BadLinqExpressionException>(() => theSession.Query<JoinCustomer>()
            .LeftJoin(theSession.Query<JoinOrder>(), c => c.Id, o => o.CustomerId,
                (c, o) => new JoinedRow(c.Name, o.Status, (decimal?)o.Amount))
            .Where(x => x.Amount > 150m)
            .ToListAsync());
        left.Message.ShouldContain("Where() on the inner side of a left join");

        // RightJoin is the left join with the sources swapped, so its nullable side is the outer one
        var right = await Should.ThrowAsync<BadLinqExpressionException>(() => theSession.Query<JoinCustomer>()
            .RightJoin(theSession.Query<JoinOrder>(), c => c.Id, o => o.CustomerId,
                (c, o) => new JoinedRow(c.Name, o.Status, o.Amount))
            .Where(x => x.Customer == null)
            .ToListAsync());
        right.Message.ShouldContain("Where() on the inner side of a left join");

        // The hand-written form had the same problem
        var handWritten = await Should.ThrowAsync<BadLinqExpressionException>(() => theSession.Query<JoinCustomer>()
            .GroupJoin(theSession.Query<JoinOrder>(), c => c.Id, o => o.CustomerId, (c, orders) => new { c, orders })
            .SelectMany(x => x.orders.DefaultIfEmpty(), (x, o) => new { x.c.Name, Amount = (decimal?)o.Amount })
            .Where(x => x.Amount == null)
            .ToListAsync());
        handWritten.Message.ShouldContain("Where() on the inner side of a left join");
    }

    public class CustomersSharingACity: ICompiledListQuery<JoinCustomer, string>
    {
        public Expression<Func<IMartenQueryable<JoinCustomer>, IEnumerable<string>>> QueryIs()
        {
            return q => q.Join(q, a => a.City, b => b.City, (a, b) => b.Name);
        }
    }

    [Fact]
    public async Task a_join_in_a_compiled_query_is_refused()
    {
        // A compiled query has no session.Query<T>() to join to: its only source is its parameter
        var ex = await Should.ThrowAsync<Exception>(() => theSession.QueryAsync(new CustomersSharingACity()));

        var message = ex is BadLinqExpressionException ? ex.Message : ex.InnerException?.Message ?? ex.Message;
        message.ShouldContain("the inner source must be a Marten session.Query<T>()");
        message.ShouldContain("compiled query's parameter");
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
    }

    [Fact]
    public async Task the_tuple_full_join_overload_is_refused_too()
    {
        var message = await refusalFor(theSession.Query<JoinCustomer>()
            .FullJoin(theSession.Query<JoinOrder>(), c => c.Id, o => o.CustomerId));

        message.ShouldContain("Queryable.FullJoin()");
        message.ShouldContain("no FULL OUTER JOIN translation");
    }
#endif
}
