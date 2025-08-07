using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using LinqTests.Acceptance.Support;
using Marten;
using Marten.Services.Json;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Acceptance;

public class select_clause_usage: IntegrationContext
{
    #region sample_one_field_projection
    [Fact]
    public async Task use_select_in_query_for_one_field()
    {
        theSession.Store(new User { FirstName = "Hank" });
        theSession.Store(new User { FirstName = "Bill" });
        theSession.Store(new User { FirstName = "Sam" });
        theSession.Store(new User { FirstName = "Tom" });

        await theSession.SaveChangesAsync();

        theSession.Query<User>().OrderBy(x => x.FirstName).Select(x => x.FirstName)
            .ShouldHaveTheSameElementsAs("Bill", "Hank", "Sam", "Tom");
    }

    #endregion

    [Fact]
    public async Task use_select_in_query_for_one_field_and_first()
    {
        theSession.Store(new User { FirstName = "Hank" });
        theSession.Store(new User { FirstName = "Bill" });
        theSession.Store(new User { FirstName = "Sam" });
        theSession.Store(new User { FirstName = "Tom" });

        await theSession.SaveChangesAsync();

        theSession.Query<User>().OrderBy(x => x.FirstName).Select(x => x.FirstName)
            .First().ShouldBe("Bill");
    }

    [Fact]
    public async Task use_select_in_query_for_first_in_collection()
    {
        theSession.Store(new User { FirstName = "Hank", Roles = ["a", "b", "c"]});
        theSession.Store(new User { FirstName = "Bill", Roles = ["d", "b", "c"] });
        theSession.Store(new User { FirstName = "Sam", Roles = ["e", "b", "c"] });
        theSession.Store(new User { FirstName = "Tom", Roles = ["f", "b", "c"] });

        await theSession.SaveChangesAsync();

        var data = await theSession
            .Query<User>()
            .OrderBy(x => x.FirstName)
            .Select(x => new { Id = x.Id, Role = x.Roles[0] })
            .ToListAsync();

        data[0].Role.ShouldBe("d");
    }

    [Fact]
    public async Task use_select_in_query_for_one_field_async()
    {
        theSession.Store(new User { FirstName = "Hank" });
        theSession.Store(new User { FirstName = "Bill" });
        theSession.Store(new User { FirstName = "Sam" });
        theSession.Store(new User { FirstName = "Tom" });

        await theSession.SaveChangesAsync();

        var names = await theSession.Query<User>().OrderBy(x => x.FirstName).Select(x => x.FirstName).ToListAsync();
        names.ShouldHaveTheSameElementsAs("Bill", "Hank", "Sam", "Tom");
    }

    [Fact]
    public async Task use_select_to_another_type()
    {
        theSession.Store(new User { FirstName = "Hank" });
        theSession.Store(new User { FirstName = "Bill" });
        theSession.Store(new User { FirstName = "Sam" });
        theSession.Store(new User { FirstName = "Tom" });

        await theSession.SaveChangesAsync();

        theSession.Query<User>().OrderBy(x => x.FirstName).Select(x => new UserName { Name = x.FirstName })
            .ToArray()
            .Select(x => x.Name)
            .ShouldHaveTheSameElementsAs("Bill", "Hank", "Sam", "Tom");
    }


    #region sample_get_first_projection
    [Fact]
    public async Task use_select_to_another_type_with_first()
    {
        theSession.Store(new User { FirstName = "Hank" });
        theSession.Store(new User { FirstName = "Bill" });
        theSession.Store(new User { FirstName = "Sam" });
        theSession.Store(new User { FirstName = "Tom" });

        await theSession.SaveChangesAsync();

        theSession.Query<User>().OrderBy(x => x.FirstName).Select(x => new UserName { Name = x.FirstName })
            .FirstOrDefault()
            ?.Name.ShouldBe("Bill");
    }

    #endregion



    [Fact]
    public async Task use_select_to_anonymous_type_with_to_json_array()
    {
        theSession.Store(new User { FirstName = "Hank" });
        theSession.Store(new User { FirstName = "Bill" });
        theSession.Store(new User { FirstName = "Sam" });
        theSession.Store(new User { FirstName = "Tom" });

        await theSession.SaveChangesAsync();
        var json = await theSession.Query<User>().OrderBy(x => x.FirstName).Select(x => new { Name = x.FirstName })
            .ToJsonArray();
        json.ShouldBe("[{\"Name\": \"Bill\"},{\"Name\": \"Hank\"},{\"Name\": \"Sam\"},{\"Name\": \"Tom\"}]");
    }

    [Fact]
    public async Task use_select_to_another_type_async()
    {
        theSession.Store(new User { FirstName = "Hank" });
        theSession.Store(new User { FirstName = "Bill" });
        theSession.Store(new User { FirstName = "Sam" });
        theSession.Store(new User { FirstName = "Tom" });

        await theSession.SaveChangesAsync();

        var users = await theSession
            .Query<User>()
            .OrderBy(x => x.FirstName)
            .Select(x => new UserName { Name = x.FirstName })
            .ToListAsync();

        users.Select(x => x.Name)
            .ShouldHaveTheSameElementsAs("Bill", "Hank", "Sam", "Tom");
    }

    [Fact]
    public async Task use_select_to_another_type_as_to_json_array()
    {
        theSession.Store(new User { FirstName = "Hank" });
        theSession.Store(new User { FirstName = "Bill" });
        theSession.Store(new User { FirstName = "Sam" });
        theSession.Store(new User { FirstName = "Tom" });

        await theSession.SaveChangesAsync();
        var users = await theSession
            .Query<User>()
            .OrderBy(x => x.FirstName)
            .Select(x => new UserName { Name = x.FirstName })
            .ToJsonArray();

        users.ShouldBe("[{\"Name\": \"Bill\"},{\"Name\": \"Hank\"},{\"Name\": \"Sam\"},{\"Name\": \"Tom\"}]");
    }

    #region sample_anonymous_type_projection
    [Fact]
    public async Task use_select_to_transform_to_an_anonymous_type()
    {
        theSession.Store(new User { FirstName = "Hank" });
        theSession.Store(new User { FirstName = "Bill" });
        theSession.Store(new User { FirstName = "Sam" });
        theSession.Store(new User { FirstName = "Tom" });

        await theSession.SaveChangesAsync();

        theSession.Query<User>().OrderBy(x => x.FirstName).Select(x => new { Name = x.FirstName })
            .ToArray()
            .Select(x => x.Name)
            .ShouldHaveTheSameElementsAs("Bill", "Hank", "Sam", "Tom");
    }

    #endregion

    [Fact]
    public async Task use_select_with_multiple_fields_in_anonymous()
    {
        theSession.Store(new User { FirstName = "Hank", LastName = "Aaron" });
        theSession.Store(new User { FirstName = "Bill", LastName = "Laimbeer" });
        theSession.Store(new User { FirstName = "Sam", LastName = "Mitchell" });
        theSession.Store(new User { FirstName = "Tom", LastName = "Chambers" });

        await theSession.SaveChangesAsync();

        var users = theSession.Query<User>().Select(x => new { First = x.FirstName, Last = x.LastName }).ToList();

        users.Count.ShouldBe(4);

        users.Each(x =>
        {
            x.First.ShouldNotBeNull();
            x.Last.ShouldNotBeNull();
        });
    }

    #region sample_other_type_projection
    [SerializerTypeTargetedFact(RunFor = SerializerType.Newtonsoft)]
    public async Task use_select_with_multiple_fields_to_other_type()
    {
        theSession.Store(new User { FirstName = "Hank", LastName = "Aaron" });
        theSession.Store(new User { FirstName = "Bill", LastName = "Laimbeer" });
        theSession.Store(new User { FirstName = "Sam", LastName = "Mitchell" });
        theSession.Store(new User { FirstName = "Tom", LastName = "Chambers" });

        await theSession.SaveChangesAsync();

        var users = theSession.Query<User>().Select(x => new User2 { First = x.FirstName, Last = x.LastName }).ToList();

        users.Count.ShouldBe(4);

        users.Each(x =>
        {
            x.First.ShouldNotBeNull();
            x.Last.ShouldNotBeNull();
        });
    }

    #endregion

    public class User2
    {
        public string First;
        public string Last;
    }


    [SerializerTypeTargetedFact(RunFor = SerializerType.Newtonsoft)]
    public async Task use_select_with_multiple_fields_to_other_type_using_constructor()
    {
        theSession.Store(new User { FirstName = "Hank", LastName = "Aaron" });
        theSession.Store(new User { FirstName = "Bill", LastName = "Laimbeer" });
        theSession.Store(new User { FirstName = "Sam", LastName = "Mitchell" });
        theSession.Store(new User { FirstName = "Tom", LastName = "Chambers" });

        await theSession.SaveChangesAsync();

        var users = theSession.Query<User>()
            .Select(x => new UserDto(x.FirstName, x.LastName))
            .ToList();

        users.Count.ShouldBe(4);

        users.Each(x =>
        {
            x.FirstName.ShouldNotBeNull();
            x.LastName.ShouldNotBeNull();
        });
    }

    [SerializerTypeTargetedFact(RunFor = SerializerType.Newtonsoft)]
    public async Task use_select_with_multiple_fields_to_other_type_using_constructor_and_properties()
    {
        theSession.Store(new User { FirstName = "Hank", LastName = "Aaron", Age = 20 });
        theSession.Store(new User { FirstName = "Bill", LastName = "Laimbeer", Age = 40 });
        theSession.Store(new User { FirstName = "Sam", LastName = "Mitchell", Age = 60 });
        theSession.Store(new User { FirstName = "Tom", LastName = "Chambers", Age = 80 });

        await theSession.SaveChangesAsync();

        var users = theSession.Query<User>()
            .Select(x => new UserDto(x.FirstName, x.LastName) { YearsOld = x.Age })
            .ToList();

        users.Count.ShouldBe(4);

        users.Each(x =>
        {
            x.FirstName.ShouldNotBeNull();
            x.LastName.ShouldNotBeNull();
            x.YearsOld.ShouldBeGreaterThan(0);
        });
    }

    public class UserDto
    {
        public UserDto(string firstName, string lastName)
        {
            FirstName = firstName;
            LastName = lastName;
        }

        public string FirstName { get; }
        public string LastName { get; }
        public int YearsOld { get; set; }
    }

    [Fact]
    public async Task use_select_to_transform_to_an_anonymous_type_async()
    {
        theSession.Store(new User { FirstName = "Hank" });
        theSession.Store(new User { FirstName = "Bill" });
        theSession.Store(new User { FirstName = "Sam" });
        theSession.Store(new User { FirstName = "Tom" });

        await theSession.SaveChangesAsync();

        var users = await theSession
            .Query<User>()
            .OrderBy(x => x.FirstName)
            .Select(x => new { Name = x.FirstName })
            .ToListAsync();

        users
            .Select(x => x.Name)
            .ShouldHaveTheSameElementsAs("Bill", "Hank", "Sam", "Tom");
    }

    #region sample_deep_properties_projection
    [Fact]
    public async Task transform_with_deep_properties()
    {
        var targets = Target.GenerateRandomData(100).ToArray();

        await theStore.BulkInsertAsync(targets);

        var actual = theSession.Query<Target>().Where(x => x.Number == targets[0].Number).Select(x => x.Inner.Number).ToList().Distinct();

        var expected = targets.Where(x => x.Number == targets[0].Number).Select(x => x.Inner.Number).Distinct();

        actual.ShouldHaveTheSameElementsAs(expected);
    }

    #endregion


    [SerializerTypeTargetedFact(RunFor = SerializerType.Newtonsoft)]
    public async Task transform_with_deep_properties_to_anonymous_type()
    {
        var target = Target.Random(true);

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        var actual = theSession.Query<Target>()
            .Where(x => x.Id == target.Id)
            .Select(x => new { x.Id, x.Number, InnerNumber = x.Inner.Number })
            .First();

        actual.Id.ShouldBe(target.Id);
        actual.Number.ShouldBe(target.Number);
        actual.InnerNumber.ShouldBe(target.Inner.Number);
    }

    [SerializerTypeTargetedFact(RunFor = SerializerType.Newtonsoft)]
    public async Task transform_with_deep_properties_to_type_using_constructor()
    {
        var target = Target.Random(true);

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        var actual = theSession.Query<Target>()
            .Where(x => x.Id == target.Id)
            .Select(x => new FlatTarget(x.Id, x.Number, x.Inner.Number))
            .First();

        actual.Id.ShouldBe(target.Id);
        actual.Number.ShouldBe(target.Number);
        actual.InnerNumber.ShouldBe(target.Inner.Number);
    }

    [Fact]
    public async Task use_select_in_query_for_one_object_property()
    {
        var target = Target.Random(true);

        theSession.Store(target);
        await theSession.SaveChangesAsync();

        var actual = theSession.Query<Target>()
            .Where(x => x.Id == target.Id)
            .Select(x => x.Inner)
            .First();

        actual.Id.ShouldBe(target.Inner.Id);
        actual.Number.ShouldBe(target.Inner.Number);
    }


    public class FlatTarget
    {
        public FlatTarget(Guid id, int number, int innerNumber)
        {
            Id = id;
            Number = number;
            InnerNumber = innerNumber;
        }

        public Guid Id { get; }
        public int Number { get; }
        public int InnerNumber { get; }
    }


    public select_clause_usage(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}


public class UserName
{
    public string Name { get; set; }
}


