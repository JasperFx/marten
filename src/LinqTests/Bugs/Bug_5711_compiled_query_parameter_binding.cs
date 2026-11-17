using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Linq;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

/// <summary>
///     #5711: a compiled query's plan learns which SQL parameter comes from which member of the query
///     object by matching values. It builds the plan from one instance and pairs each parameter whose
///     value equals a member's value with that member. Two silent failures follow, both returning
///     wrong rows on the second run, which is served from the cached plan:
///     <list type="bullet">
///         <item>
///             <b>A literal equal to a member's first value is rebound to that member.</b> The plan was
///             built from the caller's own instance whenever its member values were unique among
///             members, without checking them against the query's literals. The plan is now always
///             built from a generated template whose values cannot plausibly collide with a literal.
///         </item>
///         <item>
///             <b>A value derived from members froze at plan time.</b> <c>First + Second</c> or
///             <c>Name.ToUpper()</c> equals no single member, so the plan took it for a literal. Those
///             are refused at plan time now, until they can be bound structurally.
///         </item>
///     </list>
///     Every query here runs twice with different values, because the first run always looks right.
/// </summary>
public class Bug_5711_compiled_query_parameter_binding: BugIntegrationContext
{
    public class Ticket
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? Number { get; set; }
        public DateTime Opened { get; set; }
    }

    public class NamedOrBB: ICompiledListQuery<Ticket>
    {
        public string Name { get; set; } = string.Empty;

        public Expression<Func<IMartenQueryable<Ticket>, IEnumerable<Ticket>>> QueryIs()
        {
            return q => q.Where(x => x.Name == Name || x.Name == "BB");
        }
    }

    public class NamedOrBBInArray: ICompiledListQuery<Ticket>
    {
        public string Name { get; set; } = string.Empty;

        public Expression<Func<IMartenQueryable<Ticket>, IEnumerable<Ticket>>> QueryIs()
        {
            return q => q.Where(x => new[] { Name, "BB" }.Contains(x.Name));
        }
    }

    public class NamedConcatenation: ICompiledListQuery<Ticket>
    {
        public string First { get; set; } = string.Empty;
        public string Second { get; set; } = string.Empty;

        public Expression<Func<IMartenQueryable<Ticket>, IEnumerable<Ticket>>> QueryIs()
        {
            return q => q.Where(x => x.Name == First + Second);
        }
    }

    public class NamedUpper: ICompiledListQuery<Ticket>
    {
        public string Name { get; set; } = string.Empty;

        public Expression<Func<IMartenQueryable<Ticket>, IEnumerable<Ticket>>> QueryIs()
        {
            return q => q.Where(x => x.Name == Name.ToUpper());
        }
    }

    public class NamedInList: ICompiledListQuery<Ticket>
    {
        public string First { get; set; } = string.Empty;
        public string Second { get; set; } = string.Empty;

        public Expression<Func<IMartenQueryable<Ticket>, IEnumerable<Ticket>>> QueryIs()
        {
            return q => q.Where(x => new List<string> { First, Second }.Contains(x.Name));
        }
    }

    // C# lifts the int member to int? for the comparison: Convert(this.Number), which still binds
    public class NumberedLifted: ICompiledListQuery<Ticket>
    {
        public int Number { get; set; }

        public Expression<Func<IMartenQueryable<Ticket>, IEnumerable<Ticket>>> QueryIs()
        {
            return q => q.Where(x => x.Number == Number);
        }
    }

    public enum Priority
    {
        None,
        Low,
        Medium,
        High
    }

    // An enum member cast to int is the same value the plan matches it by, so it still binds
    public class NumberedByPriority: ICompiledListQuery<Ticket>
    {
        public Priority Priority { get; set; }

        public Expression<Func<IMartenQueryable<Ticket>, IEnumerable<Ticket>>> QueryIs()
        {
            return q => q.Where(x => x.Number == (int)Priority);
        }
    }

    // Capturing a local as well as the query object makes the compiler read the query through the
    // closure's captured `this`, which still counts as reading the member directly
    public class NamedOrLocal: ICompiledListQuery<Ticket>
    {
        public string Name { get; set; } = string.Empty;

        public Expression<Func<IMartenQueryable<Ticket>, IEnumerable<Ticket>>> QueryIs()
        {
            var other = "CD";
            return q => q.Where(x => x.Name == Name || x.Name == other);
        }
    }

    public class NamedPlusLocal: ICompiledListQuery<Ticket>
    {
        public string Name { get; set; } = string.Empty;

        public Expression<Func<IMartenQueryable<Ticket>, IEnumerable<Ticket>>> QueryIs()
        {
            var suffix = "B";
            return q => q.Where(x => x.Name == Name + suffix);
        }
    }

    public class OpenedBetween: ICompiledListQuery<Ticket>
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }

        public Expression<Func<IMartenQueryable<Ticket>, IEnumerable<Ticket>>> QueryIs()
        {
            return q => q.Where(x => x.Opened >= From && x.Opened < To);
        }
    }

    private static readonly DateTime Day = new(2026, 10, 1);

    private async Task seedAsync()
    {
        StoreOptions(_ => { });
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Ticket));

        var number = 0;
        theSession.Store(new[] { "AB", "CD", "AA", "BB", "aa" }
            .Select(name => new Ticket { Name = name, Number = ++number, Opened = Day.AddDays(number) })
            .ToArray());
        await theSession.SaveChangesAsync();
    }

    private static string[] names(IEnumerable<Ticket> tickets) => tickets.Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();

    [Fact]
    public async Task a_literal_equal_to_the_first_value_is_not_rebound_to_the_member()
    {
        await seedAsync();

        names(await theSession.QueryAsync(new NamedOrBB { Name = "BB" })).ShouldBe(["BB"]);
        names(await theSession.QueryAsync(new NamedOrBB { Name = "aa" })).ShouldBe(["BB", "aa"]);
    }

    [Fact]
    public async Task an_array_literal_element_equal_to_the_first_value_is_not_rebound_to_the_member()
    {
        await seedAsync();

        names(await theSession.QueryAsync(new NamedOrBBInArray { Name = "BB" })).ShouldBe(["BB"]);
        names(await theSession.QueryAsync(new NamedOrBBInArray { Name = "aa" })).ShouldBe(["BB", "aa"]);
    }

    [Fact]
    public async Task a_value_concatenated_from_members_is_refused()
    {
        await seedAsync();

        var failure = await Should.ThrowAsync<InvalidCompiledQueryException>(() =>
            theSession.QueryAsync(new NamedConcatenation { First = "A", Second = "B" }));
        failure.Message.ShouldContain(nameof(NamedConcatenation));
    }

    [Fact]
    public async Task a_value_computed_by_a_method_on_a_member_is_refused()
    {
        await seedAsync();

        await Should.ThrowAsync<InvalidCompiledQueryException>(() =>
            theSession.QueryAsync(new NamedUpper { Name = "aa" }));
    }

    [Fact]
    public async Task a_list_initializer_of_members_is_refused_by_name()
    {
        await seedAsync();

        await Should.ThrowAsync<InvalidCompiledQueryException>(() =>
            theSession.QueryAsync(new NamedInList { First = "AB", Second = "CD" }));
    }

    [Fact]
    public async Task a_member_lifted_to_nullable_still_binds()
    {
        await seedAsync();

        names(await theSession.QueryAsync(new NumberedLifted { Number = 1 })).ShouldBe(["AB"]);
        names(await theSession.QueryAsync(new NumberedLifted { Number = 2 })).ShouldBe(["CD"]);
    }

    [Fact]
    public async Task an_enum_member_cast_to_its_number_still_binds()
    {
        await seedAsync();

        names(await theSession.QueryAsync(new NumberedByPriority { Priority = Priority.Medium })).ShouldBe(["CD"]);
        names(await theSession.QueryAsync(new NumberedByPriority { Priority = Priority.High })).ShouldBe(["AA"]);
    }

    [Fact]
    public async Task a_member_read_through_a_closure_still_binds()
    {
        await seedAsync();

        names(await theSession.QueryAsync(new NamedOrLocal { Name = "AB" })).ShouldBe(["AB", "CD"]);
        names(await theSession.QueryAsync(new NamedOrLocal { Name = "AA" })).ShouldBe(["AA", "CD"]);
    }

    [Fact]
    public async Task a_value_computed_through_a_closure_is_refused()
    {
        await seedAsync();

        var failure = await Should.ThrowAsync<InvalidCompiledQueryException>(() =>
            theSession.QueryAsync(new NamedPlusLocal { Name = "A" }));
        failure.Message.ShouldContain("query.Name");
    }

    // Planning from a generated template needs a distinct planning value per member, and the
    // DateTime and DateTimeOffset value sources handed out the same one to every member
    [Fact]
    public async Task two_date_members_still_bind_independently()
    {
        await seedAsync();

        names(await theSession.QueryAsync(new OpenedBetween { From = Day.AddDays(1), To = Day.AddDays(3) }))
            .ShouldBe(["AB", "CD"]);
        names(await theSession.QueryAsync(new OpenedBetween { From = Day.AddDays(4), To = Day.AddDays(6) }))
            .ShouldBe(["BB", "aa"]);
    }
}
