using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using LinqTests.Acceptance.Support;
using Marten.Testing.Documents;
using Xunit.Abstractions;

namespace LinqTests.Acceptance;

public class select_clauses : LinqTestContext<select_clauses>
{
    public select_clauses(DefaultQueryFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        TestOutput = output;
    }

    private static void select<T>(Expression<Func<Target, T>> selector)
    {
        testCases.Add(new SelectTransform<T>(selector));
    }

    static select_clauses()
    {
        var number = 10;

        select(x => new {Id = x.Id});
        select(x => new {Foo = x.Id});
        select(x => new {Id = x.Id, Inner = x.Inner});
        select(x => new {Id = x.Id, Number = x.Number});
        select(x => new {Id = x.Id, Other = x.NumberArray});
        select(x => new {Id = x.Id, Other = x.Color});
        select(x => new {Id = x.Id, Other = x.Children});
        select(x => new {Id = x.Id, Other = x.Date});
        select(x => new {Id = x.Id, Other = x.Decimal});
        select(x => new {Id = x.Id, Other = x.Double});
        select(x => new {Id = x.Id, Other = x.Flag});
        select(x => new {Id = x.Id, Other = x.Double});
        select(x => new {Id = x.Id, Other = x.Long});
        select(x => new {Id = x.Id, Other = x.DateOffset});
        select(x => new {Id = x.Id, Other = x.GuidArray});
        select(x => new {Id = x.Id, Other = x.GuidDict});
        select(x => new {Id = x.Id, Other = x.Float});
        select(x => new {Id = x.Id, Other = x.NullableBoolean});
        select(x => new {Id = x.Id, Other = x.NullableColor});
        select(x => new {Id = x.Id, Other = x.StringArray});
        select(x => new {Id = x.Id, Other = x.StringDict});
        select(x => new {Id = x.Id, Other = x.TagsHashSet});
        select(x => new {Id = x.Id, Name = x.String});
        select(x => new {Id = x.Id, Name = "Harold"});
        select(x => new {Id = x.Inner.Number, Name = x.Inner.String});
        select(x => new {Id = 5, Name = x.Inner.String});
        select(x => new {Id = number, Name = x.Inner.String});
        select(x => new { Id = x.Id, Name = x.StringArray[0] });
        select(x => new { Id = x.Id, Age = x.NumberArray[0] });

        select(x => new Person { Age = x.Number, Name = x.String });
        select(x => new Person(x.String, x.Number));

        select(x => new { Id = x.Id, Person = new Person { Age = x.Number, Name = x.String } });
        select(x => new { Id = x.Id, Person = new Person(x.String, x.Number) });
    }

    [Theory]
    [MemberData(nameof(GetDescriptions))]
    public Task run_query(string description)
    {
        return assertTestCase(description, Fixture.Store);
    }

    public class Person
    {
        public Person()
        {
        }

        public Person(string name, int age)
        {
            Name = name;
            Age = age;
        }

        public string Name { get; set; }
        public int Age { get; set; }
    }
}
