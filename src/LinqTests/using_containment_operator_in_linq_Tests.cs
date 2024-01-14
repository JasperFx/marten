using System.Linq;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;

namespace LinqTests;

public class using_containment_operator_in_linq_Tests: OneOffConfigurationsContext
{
    public using_containment_operator_in_linq_Tests()
    {
        StoreOptions(_ => { _.Schema.For<Target>().GinIndexJsonData(); });
    }

    [Fact]
    public void query_by_date()
    {
        var targets = Target.GenerateRandomData(6).ToArray();
        using var session = TheStore.IdentitySession();
        session.Store(targets);

        session.SaveChanges();

        var actual = session.Query<Target>().Where(x => x.Date == targets.ElementAt(2).Date)
            .ToArray();

        SpecificationExtensions.ShouldBeGreaterThan(actual.Length, 0);


        actual.ShouldContain(targets.ElementAt(2));
    }

    [Fact]
    public void query_by_number()
    {
        using var session = TheStore.IdentitySession();
        session.Store(new Target { Number = 1 });
        session.Store(new Target { Number = 2 });
        session.Store(new Target { Number = 3 });
        session.Store(new Target { Number = 4 });
        session.Store(new Target { Number = 5 });
        session.Store(new Target { Number = 6 });

        session.SaveChanges();


        session.Query<Target>().Where(x => x.Number == 3).Single().Number.ShouldBe(3);
    }

    [Fact]
    public void query_by_string()
    {
        using var session = TheStore.IdentitySession();
        session.Store(new Target { String = "Python" });
        session.Store(new Target { String = "Ruby" });
        session.Store(new Target { String = "Java" });
        session.Store(new Target { String = "C#" });
        session.Store(new Target { String = "Scala" });

        session.SaveChanges();

        session.Query<Target>().Where(x => x.String == "Python").Single().String.ShouldBe("Python");
    }
}

public class using_containment_operator_in_linq_with_camel_casing_Tests: OneOffConfigurationsContext
{
    public using_containment_operator_in_linq_with_camel_casing_Tests()
    {
        StoreOptions(_ =>
        {
            _.UseDefaultSerialization(EnumStorage.AsString, Casing.CamelCase);

            _.Schema.For<Target>().GinIndexJsonData();
        });
    }

    [Fact]
    public void query_by_date()
    {
        using var session = TheStore.IdentitySession();

        var targets = Target.GenerateRandomData(6).ToArray();
        session.Store(targets);

        session.SaveChanges();

        var actual = session.Query<Target>().Where(x => x.Date == targets.ElementAt(2).Date)
            .ToArray();

        SpecificationExtensions.ShouldBeGreaterThan(actual.Length, 0);

        actual.ShouldContain(targets.ElementAt(2));
    }

    [Fact]
    public void query_by_number()
    {
        using var session = TheStore.IdentitySession();
        session.Store(new Target { Number = 1 });
        session.Store(new Target { Number = 2 });
        session.Store(new Target { Number = 3 });
        session.Store(new Target { Number = 4 });
        session.Store(new Target { Number = 5 });
        session.Store(new Target { Number = 6 });

        session.SaveChanges();


        session.Query<Target>().Where(x => x.Number == 3).Single().Number.ShouldBe(3);
    }

    [Fact]
    public void query_by_string()
    {
        using var session = TheStore.IdentitySession();
        session.Store(new Target { String = "Python" });
        session.Store(new Target { String = "Ruby" });
        session.Store(new Target { String = "Java" });
        session.Store(new Target { String = "C#" });
        session.Store(new Target { String = "Scala" });

        session.SaveChanges();

        session.Query<Target>().Where(x => x.String == "Python").Single().String.ShouldBe("Python");
    }
}
