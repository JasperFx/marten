using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Reading.Json;

public class SimpleUser
{
    public SimpleUser()
    {
        Id = Guid.NewGuid();
    }

    public Guid Id { get; set; }
    public string UserName { get; set; }
    public DateTime Birthdate { get; set; }
    public int Number { get; set; }
    public SimpleAddress Address { get; set; }

    public string ToJson()
    {
        return $@"
{{
""Id"": ""{Id}"", ""Number"": {Number}, ""Address"":
{{
""Street"": ""{Address.Street}"", ""HouseNumber"": ""{Address.HouseNumber}""
}},
""UserName"": ""{UserName}"",
""Birthdate"": ""{Birthdate.ToString("s")}""
}}".Replace("\r\n", "").Replace("\n", "");
    }
}

public class SimpleAddress
{
    public string Street { get; set; }
    public string HouseNumber { get; set; }
}

public class streaming_json_results : IntegrationContext
{
    public streaming_json_results(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    protected override async Task fixtureSetup()
    {
        await theStore.Advanced.ResetAllData();
    }

    private T deserialize<T>(Stream stream)
    {
        stream.Position = 0;

        return theStore.Serializer.FromJson<T>(stream);
    }

    [Fact]
    public async Task stream_by_id_miss()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(IntDoc));

        var stream = new MemoryStream();
        var found = await theSession.Json.StreamById<IntDoc>(1, stream);
        found.ShouldBeFalse();
    }

    [Fact]
    public async Task stream_by_int_id_hit()
    {
        var doc = new IntDoc();
        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        var stream = new MemoryStream();
        var found = await theSession.Json.StreamById<IntDoc>(doc.Id, stream);
        found.ShouldBeTrue();

        var target = deserialize<IntDoc>(stream);
        target.Id.ShouldBe(doc.Id);
    }

    [Fact]
    public async Task stream_by_long_id_hit()
    {
        var doc = new LongDoc();
        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        var stream = new MemoryStream();
        var found = await theSession.Json.StreamById<LongDoc>(doc.Id, stream);
        found.ShouldBeTrue();

        var target = deserialize<LongDoc>(stream);
        target.Id.ShouldBe(doc.Id);
    }

    [Fact]
    public async Task stream_by_string_id_hit()
    {
        var doc = new StringDoc{Id = Guid.NewGuid().ToString()};
        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        var stream = new MemoryStream();
        var found = await theSession.Json.StreamById<StringDoc>(doc.Id, stream);
        found.ShouldBeTrue();

        var target = deserialize<StringDoc>(stream);
        target.Id.ShouldBe(doc.Id);
    }

    [Fact]
    public async Task stream_by_Guid_id_hit()
    {
        var doc = new GuidDoc{};
        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        var stream = new MemoryStream();
        var found = await theSession.Json.StreamById<GuidDoc>(doc.Id, stream);
        found.ShouldBeTrue();

        var target = deserialize<GuidDoc>(stream);
        target.Id.ShouldBe(doc.Id);
    }

    [Fact]
    public async Task stream_one_with_linq_miss()
    {
        var stream = new MemoryStream();
        var found = await theSession.Query<Target>().Where(x => x.Id == Guid.NewGuid())
            .StreamJsonFirstOrDefault(stream);

        found.ShouldBeFalse();
    }

    [Fact]
    public async Task stream_one_with_linq_hit()
    {
        var targets = Target.GenerateRandomData(10).ToArray();
        await theStore.BulkInsertAsync(targets);

        var stream = new MemoryStream();
        var found = await theSession.Query<Target>().Where(x => x.Id == targets[3].Id)
            .StreamJsonFirstOrDefault(stream);

        found.ShouldBeTrue();

        var target = deserialize<Target>(stream);

        target.Id.ShouldBe(targets[3].Id);
    }

    [Fact]
    public async Task stream_many_with_no_hits()
    {
        var stream = new MemoryStream();
        await theSession.Query<Target>().Where(x => x.Id == Guid.NewGuid())
            .StreamJsonArray(stream);

        var targets = deserialize<Target[]>(stream);
        targets.Any().ShouldBeFalse();
    }

    [Fact]
    public async Task stream_many_with_multiple_hits()
    {
        var targets = Target.GenerateRandomData(10).ToArray();
        await theStore.BulkInsertAsync(targets);

        var stream = new MemoryStream();
        await theSession.Query<Target>().Take(5)
            .StreamJsonArray(stream);

        var results = deserialize<Target[]>(stream);
        results.Length.ShouldBe(5);
    }

    [Fact]
    public async Task stream_many_with_one_hit()
    {
        var targets = Target.GenerateRandomData(10).ToArray();
        await theStore.BulkInsertAsync(targets);

        var stream = new MemoryStream();
        await theSession.Query<Target>().Take(1)
            .StreamJsonArray(stream);

        var results = deserialize<Target[]>(stream);
        results.Length.ShouldBe(1);
    }

    [Fact]
    public async Task to_list()
    {
        var user0 = new SimpleUser
        {
            UserName = "Invisible man",
            Number = 4,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress {HouseNumber = "0", Street = "rue de l'invisible"}
        };
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        var user2 = new SimpleUser
        {
            UserName = "Mrs Fouine",
            Number = 6,
            Birthdate = new DateTime(1987, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        theSession.Store(user0,user1,user2);
        await theSession.SaveChangesAsync();

        var listJson = await theSession.Query<SimpleUser>().Where(x=>x.Number>=5).ToJsonArray();

        listJson.ShouldBeSemanticallySameJsonAs($@"[{user1.ToJson()},{user2.ToJson()}]");
    }

    [Fact]
    public async Task to_list_async()
    {
        var user0 = new SimpleUser
        {
            UserName = "Invisible man",
            Number = 4,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress {HouseNumber = "0", Street = "rue de l'invisible"}
        };
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        var user2 = new SimpleUser
        {
            UserName = "Mrs Fouine",
            Number = 6,
            Birthdate = new DateTime(1987, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        theSession.Store(user0,user1,user2);
        await theSession.SaveChangesAsync();
        var listJson = await theSession.Query<SimpleUser>().Where(x=>x.Number>=5).ToJsonArray();
        listJson.ShouldBeSemanticallySameJsonAs($@"[{user1.ToJson()},{user2.ToJson()}]");
    }

    [Fact]
    public async Task first_returns_first()
    {
        var user0 = new SimpleUser
        {
            UserName = "Invisible man",
            Number = 4,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress {HouseNumber = "0", Street = "rue de l'invisible"}
        };
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        var user2 = new SimpleUser
        {
            UserName = "Mrs Fouine",
            Number = 5,
            Birthdate = new DateTime(1987, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        theSession.Store(user0,user1,user2);
        await theSession.SaveChangesAsync();

        var userJson = await theSession.Query<SimpleUser>().Where(x => x.Number == 5).ToJsonFirst();
        userJson.ShouldBeSemanticallySameJsonAs($@"{user1.ToJson()}");
    }

    [Fact]
    public async Task first_returns_first_line()
    {
        var user0 = new SimpleUser
        {
            UserName = "Invisible man",
            Number = 4,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress {HouseNumber = "0", Street = "rue de l'invisible"}
        };
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        var user2 = new SimpleUser
        {
            UserName = "Mrs Fouine",
            Number = 5,
            Birthdate = new DateTime(1987, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        theSession.Store(user0,user1,user2);
        await theSession.SaveChangesAsync();
        var userJson = await theSession.Query<SimpleUser>().ToJsonFirst();
        userJson.ShouldBeSemanticallySameJsonAs($@"{user0.ToJson()}");
    }

    [Fact]
    public async Task first_throws_when_none_returned()
    {
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        var user2 = new SimpleUser
        {
            UserName = "Mrs Fouine",
            Number = 5,
            Birthdate = new DateTime(1987, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        theSession.Store(user1,user2);
        await theSession.SaveChangesAsync();
        var ex = await Should.ThrowAsync<InvalidOperationException>(async () => await theSession.Query<SimpleUser>().Where(x => x.Number != 5).ToJsonFirst());
        ex.Message.ShouldBe("Sequence contains no elements");
    }

    [Fact]
    public async Task first_async_returns_first()
    {
        var user0 = new SimpleUser
        {
            UserName = "Invisible man",
            Number = 4,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress {HouseNumber = "0", Street = "rue de l'invisible"}
        };
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        var user2 = new SimpleUser
        {
            UserName = "Mrs Fouine",
            Number = 5,
            Birthdate = new DateTime(1987, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        theSession.Store(user0,user1,user2);
        await theSession.SaveChangesAsync();
        var userJson = await theSession.Query<SimpleUser>().Where(x => x.Number == 5).ToJsonFirst();
        userJson.ShouldBeSemanticallySameJsonAs($@"{user1.ToJson()}");
    }

    [Fact]
    public async Task first_async_returns_first_line()
    {
        var user0 = new SimpleUser
        {
            UserName = "Invisible man",
            Number = 4,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress {HouseNumber = "0", Street = "rue de l'invisible"}
        };
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        var user2 = new SimpleUser
        {
            UserName = "Mrs Fouine",
            Number = 5,
            Birthdate = new DateTime(1987, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        theSession.Store(user0,user1,user2);
        await theSession.SaveChangesAsync();

        var userJson = await theSession.Query<SimpleUser>().ToJsonFirst();
        userJson.ShouldBeSemanticallySameJsonAs($@"{user0.ToJson()}");
    }

    [Fact]
    public async Task first_async_throws_when_none_returned()
    {
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        var user2 = new SimpleUser
        {
            UserName = "Mrs Fouine",
            Number = 5,
            Birthdate = new DateTime(1987, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        theSession.Store(user1,user2);
        await theSession.SaveChangesAsync();
        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            theSession.Query<SimpleUser>().Where(x => x.Number != 5).ToJsonFirst());
        ex.Message.ShouldBe("Sequence contains no elements");
    }

    [Fact]
    public async Task first_or_default_returns_first()
    {
        var user0 = new SimpleUser
        {
            UserName = "Invisible man",
            Number = 4,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress {HouseNumber = "0", Street = "rue de l'invisible"}
        };
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        var user2 = new SimpleUser
        {
            UserName = "Mrs Fouine",
            Number = 5,
            Birthdate = new DateTime(1987, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        theSession.Store(user0,user1,user2);
        await theSession.SaveChangesAsync();
        var userJson = await theSession.Query<SimpleUser>().Where(x => x.Number == 5).ToJsonFirstOrDefault();
        userJson.ShouldBeSemanticallySameJsonAs($@"{user1.ToJson()}");
    }

    [Fact]
    public async Task first_or_default_returns_first_line()
    {
        var user0 = new SimpleUser
        {
            UserName = "Invisible man",
            Number = 4,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress {HouseNumber = "0", Street = "rue de l'invisible"}
        };
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        var user2 = new SimpleUser
        {
            UserName = "Mrs Fouine",
            Number = 5,
            Birthdate = new DateTime(1987, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        theSession.Store(user0,user1,user2);
        await theSession.SaveChangesAsync();
        var userJson = await theSession.Query<SimpleUser>().ToJsonFirstOrDefault();
        userJson.ShouldBeSemanticallySameJsonAs($@"{user0.ToJson()}");
    }

    [Fact]
    public async Task first_or_default_returns_first_async()
    {
        var user0 = new SimpleUser
        {
            UserName = "Invisible man",
            Number = 4,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress {HouseNumber = "0", Street = "rue de l'invisible"}
        };
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        var user2 = new SimpleUser
        {
            UserName = "Mrs Fouine",
            Number = 5,
            Birthdate = new DateTime(1987, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        theSession.Store(user0,user1,user2);
        await theSession.SaveChangesAsync();
        var userJson = await theSession.Query<SimpleUser>().Where(x => x.Number == 5).ToJsonFirstOrDefault();
        userJson.ShouldBeSemanticallySameJsonAs($@"{user1.ToJson()}");
    }

    [Fact]
    public async Task first_or_default_returns_first_line_async()
    {
        var user0 = new SimpleUser
        {
            UserName = "Invisible man",
            Number = 4,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress {HouseNumber = "0", Street = "rue de l'invisible"}
        };
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        var user2 = new SimpleUser
        {
            UserName = "Mrs Fouine",
            Number = 5,
            Birthdate = new DateTime(1987, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        theSession.Store(user0,user1,user2);
        await theSession.SaveChangesAsync();
        var userJson = await theSession.Query<SimpleUser>().ToJsonFirstOrDefault();
        userJson.ShouldBeSemanticallySameJsonAs($@"{user0.ToJson()}");
    }

    [Fact]
    public async Task first_or_default_returns_default()
    {
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        var user2 = new SimpleUser
        {
            UserName = "Mrs Fouine",
            Number = 5,
            Birthdate = new DateTime(1987, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        theSession.Store(user1,user2);
        await theSession.SaveChangesAsync();
        var userJson = await theSession.Query<SimpleUser>().Where(x=>x.Number != 5).ToJsonFirstOrDefault();
        userJson.ShouldBeNull();
    }

    [Fact]
    public async Task first_or_default_async_returns_default()
    {
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        var user2 = new SimpleUser
        {
            UserName = "Mrs Fouine",
            Number = 5,
            Birthdate = new DateTime(1987, 10, 4),
            Address = new SimpleAddress {HouseNumber = "12bis", Street = "rue de la martre"}
        };
        theSession.Store(user1,user2);
        await theSession.SaveChangesAsync();
        var userJson = await theSession.Query<SimpleUser>().Where(x=>x.Number != 5).ToJsonFirstOrDefault();
        userJson.ShouldBeNull();
    }

    public class Customer
    {
        public Guid Id { get; set; }
        public string LastName { get; set; }
    }

    public class FindCustomerJsonByNameQuery : ICompiledListQuery<Customer>
    {
        public string LastNamePrefix { get; set; } = string.Empty;

        Expression<Func<IMartenQueryable<Customer>, IEnumerable<Customer>>> ICompiledQuery<Customer, IEnumerable<Customer>>.QueryIs()
        {
            return q => q
                .Where(p => p.LastName.StartsWith(LastNamePrefix))
                .OrderBy(p => p.LastName);
        }
    }

    [Fact]
    public async Task streaming_compiled_query_list_to_JSON()
    {
        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();

        var customer1 = new Customer{LastName = "Sir Mixalot"};
        var customer2 = new Customer{LastName = "Sir Gawain"};
        var customer3 = new Customer{LastName = "Ser Jaime"};

        theSession.Store(customer1, customer2, customer3);
        await theSession.SaveChangesAsync();

        var customerJson = await theSession.ToJsonMany(new FindCustomerJsonByNameQuery {LastNamePrefix = "Sir"});
        var stream = new MemoryStream();
        var writer = new StreamWriter(stream);
        await writer.WriteAsync(customerJson);
        await writer.FlushAsync();
        stream.Position = 0;
        var customers = theStore.Options.Serializer().FromJson<Customer[]>(stream);
        customers.Length.ShouldBe(2);

        customerJson.Length.ShouldBe(148); // magic number that just happens to be the length of the JSON string returned
    }



    [Fact]
    public async Task single_returns_only_match()
    {
        var user0 = new SimpleUser
        {
            UserName = "Invisible man",
            Number = 4,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress { HouseNumber = "0", Street = "rue de l'invisible" }
        };
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        theSession.Store(user0, user1);
        await theSession.SaveChangesAsync();
        var userJson = await theSession.Query<SimpleUser>().Where(x => x.Number == 5).ToJsonSingle();
        userJson.ShouldBeSemanticallySameJsonAs($@"{user1.ToJson()}");
    }

    [Fact]
    public async Task single_returns_first_and_only()
    {
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        theSession.Store(user1);
        await theSession.SaveChangesAsync();
        var userJson = await theSession.Query<SimpleUser>().ToJsonSingle();
        userJson.ShouldBeSemanticallySameJsonAs($@"{user1.ToJson()}");
    }

    [Fact]
    public async Task single_throws_when_none_found()
    {
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        var user2 = new SimpleUser
        {
            UserName = "Mrs Fouine",
            Number = 5,
            Birthdate = new DateTime(1987, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        theSession.Store(user1, user2);
        await theSession.SaveChangesAsync();
        var ex = await Should.ThrowAsync<InvalidOperationException>(async () => await theSession.Query<SimpleUser>().Where(x => x.Number != 5).ToJsonSingle());
        ex.Message.ShouldBe("Sequence contains no elements");
    }

    [Fact]
    public async Task single_throws_when_more_than_one_found()
    {
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        var user2 = new SimpleUser
        {
            UserName = "Mrs Fouine",
            Number = 5,
            Birthdate = new DateTime(1987, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        theSession.Store(user1, user2);
        await theSession.SaveChangesAsync();
        var ex = await Should.ThrowAsync<InvalidOperationException>(async () => await theSession.Query<SimpleUser>().Where(x => x.Number == 5).ToJsonSingle());
        ex.Message.ShouldBe("Sequence contains more than one element");
    }

    [Fact]
    public async Task single_async_returns_only_match()
    {
        var user0 = new SimpleUser
        {
            UserName = "Invisible man",
            Number = 4,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress { HouseNumber = "0", Street = "rue de l'invisible" }
        };
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        theSession.Store(user0, user1);
        await theSession.SaveChangesAsync();
        var userJson = await theSession.Query<SimpleUser>().Where(x => x.Number == 5).ToJsonSingle();
        userJson.ShouldBeSemanticallySameJsonAs($@"{user1.ToJson()}");
    }

    [Fact]
    public async Task single_async_returns_first_and_only()
    {
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };

        theSession.Store(user1);
        await theSession.SaveChangesAsync();
        var userJson = await theSession.Query<SimpleUser>().ToJsonSingle();
        userJson.ShouldBeSemanticallySameJsonAs($@"{user1.ToJson()}");
    }

    [Fact]
    public async Task single_async_throws_when_none_returned()
    {
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        var user2 = new SimpleUser
        {
            UserName = "Mrs Fouine",
            Number = 5,
            Birthdate = new DateTime(1987, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        theSession.Store(user1, user2);
        await theSession.SaveChangesAsync();
        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            theSession.Query<SimpleUser>().Where(x => x.Number != 5).ToJsonSingle());
        ex.Message.ShouldBe("Sequence contains no elements");
    }

    [Fact]
    public async Task single_async_throws_when_more_than_one_returned()
    {
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        var user2 = new SimpleUser
        {
            UserName = "Mrs Fouine",
            Number = 5,
            Birthdate = new DateTime(1987, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        theSession.Store(user1, user2);
        await theSession.SaveChangesAsync();
        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            theSession.Query<SimpleUser>().Where(x => x.Number == 5).ToJsonSingle());
        ex.Message.ShouldBe("Sequence contains more than one element");
    }

    [Fact]
    public async Task single_or_default_returns_first()
    {
        var user0 = new SimpleUser
        {
            UserName = "Invisible man",
            Number = 4,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress { HouseNumber = "0", Street = "rue de l'invisible" }
        };
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        theSession.Store(user0, user1);
        await theSession.SaveChangesAsync();
        var userJson = await theSession.Query<SimpleUser>().Where(x => x.Number == 5).ToJsonSingleOrDefault();
        userJson.ShouldBeSemanticallySameJsonAs($@"{user1.ToJson()}");
    }

    [Fact]
    public async Task single_or_default_returns_first_and_only_line()
    {
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        theSession.Store(user1);
        await theSession.SaveChangesAsync();
        var userJson = await theSession.Query<SimpleUser>().ToJsonSingleOrDefault();
        userJson.ShouldBeSemanticallySameJsonAs($@"{user1.ToJson()}");
    }

    [Fact]
    public async Task single_or_default_returns_first_async()
    {
        var user0 = new SimpleUser
        {
            UserName = "Invisible man",
            Number = 4,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress { HouseNumber = "0", Street = "rue de l'invisible" }
        };
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        var user2 = new SimpleUser
        {
            UserName = "Mrs Fouine",
            Number = 6,
            Birthdate = new DateTime(1987, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        theSession.Store(user0, user1, user2);
        await theSession.SaveChangesAsync();
        var userJson = await theSession.Query<SimpleUser>().Where(x => x.Number == 5).ToJsonSingleOrDefault();
        userJson.ShouldBeSemanticallySameJsonAs($@"{user1.ToJson()}");
    }

    [Fact]
    public async Task single_or_default_returns_first_line_async()
    {
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        theSession.Store(user1);
        await theSession.SaveChangesAsync();
        var userJson = await theSession.Query<SimpleUser>().ToJsonSingleOrDefault();
        userJson.ShouldBeSemanticallySameJsonAs($@"{user1.ToJson()}");
    }

    [Fact]
    public async Task single_or_default_returns_default_when_none_found()
    {
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        var user2 = new SimpleUser
        {
            UserName = "Mrs Fouine",
            Number = 5,
            Birthdate = new DateTime(1987, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        theSession.Store(user1, user2);
        await theSession.SaveChangesAsync();
        var userJson = await theSession.Query<SimpleUser>().Where(x => x.Number != 5).ToJsonSingleOrDefault();
        userJson.ShouldBeNull();
    }

    [Fact]
    public async Task single_or_default_throws_when_more_than_one_found()
    {
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        var user2 = new SimpleUser
        {
            UserName = "Mrs Fouine",
            Number = 5,
            Birthdate = new DateTime(1987, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        theSession.Store(user1, user2);
        await theSession.SaveChangesAsync();
        var ex = await Should.ThrowAsync<InvalidOperationException>(async () => await theSession.Query<SimpleUser>().Where(x => x.Number == 5).ToJsonSingleOrDefault());
        ex.Message.ShouldBe("Sequence contains more than one element");
    }

    [Fact]
    public async Task single_or_default_async_returns_default_when_none_found()
    {
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        var user2 = new SimpleUser
        {
            UserName = "Mrs Fouine",
            Number = 5,
            Birthdate = new DateTime(1987, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        theSession.Store(user1, user2);
        await theSession.SaveChangesAsync();
        var userJson = await theSession.Query<SimpleUser>().Where(x => x.Number != 5).ToJsonSingleOrDefault();
        userJson.ShouldBeNull();
    }


    [Fact]
    public async Task use_select_to_anonymous_type_with_first_as_json()
    {
        theSession.Store(new User { FirstName = "Hank" });
        theSession.Store(new User { FirstName = "Bill" });
        theSession.Store(new User { FirstName = "Sam" });
        theSession.Store(new User { FirstName = "Tom" });

        await theSession.SaveChangesAsync();

        #region sample_asjson-plus-select-2

        (await theSession
                .Query<User>()
                .OrderBy(x => x.FirstName)

                // Transform to an anonymous type
                .Select(x => new {Name = x.FirstName})

                // Select only the raw JSON
                .ToJsonFirstOrDefault())
            .ShouldBe("{\"Name\": \"Bill\"}");
        #endregion
    }

    [Fact]
    public async Task use_select_to_another_type_as_json()
    {
        theSession.Store(new User { FirstName = "Hank" });
        theSession.Store(new User { FirstName = "Bill" });
        theSession.Store(new User { FirstName = "Sam" });
        theSession.Store(new User { FirstName = "Tom" });

        await theSession.SaveChangesAsync();

        // Postgres sticks some extra spaces into the JSON string

        #region sample_asjson-plus-select-1
        var json = await theSession
            .Query<User>()
            .OrderBy(x => x.FirstName)

            // Transform the User class to a different type
            .Select(x => new UserName { Name = x.FirstName })
            .ToJsonFirst();

        json.ShouldBe("{\"Name\": \"Bill\"}");
        #endregion
    }

    public class UserName
    {
        public string Name { get; set; }
    }

    public class ColorAndId
    {
        public Guid Id { get; set; }
        public Colors Shade { get; set; }
    }

    [Fact]
    public async Task select_many_with_select_and_as_json()
    {
        var targets = Target.GenerateRandomData(100).ToArray();
        await theStore.BulkInsertAsync(targets);

        await using (var query = theStore.QuerySession())
        {
            var actualJson = await query.Query<Target>()
                .SelectMany(x => x.Children)
                .Where(x => x.Color == Colors.Green)
                .Select(x => new ColorAndId() { Id = x.Id, Shade = x.Color })
                .ToJsonArray();

            var stream = new MemoryStream();
            var writer = new StreamWriter(stream);
            await writer.WriteAsync(actualJson);
            await writer.FlushAsync();
            stream.Position = 0;
            var actual = theStore.Options.Serializer().FromJson<ColorAndId[]>(stream);

            var expected = targets
                .SelectMany(x => x.Children).Count(x => x.Color == Colors.Green);

            actual.Length.ShouldBe(expected);
        }
    }

    private class PagedEnvelope<T>
    {
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalItemCount { get; set; }
        public int PageCount { get; set; }
        public bool HasNextPage { get; set; }
        public bool HasPreviousPage { get; set; }
        public T[] Items { get; set; }
    }

    private async Task<PagedEnvelope<Target>> streamPage(IQueryable<Target> queryable, int pageNumber, int pageSize)
    {
        var stream = new MemoryStream();
        await queryable.StreamPagedJsonArray(stream, pageNumber, pageSize);
        stream.Position = 0;

        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        return await System.Text.Json.JsonSerializer.DeserializeAsync<PagedEnvelope<Target>>(stream, options);
    }

    private async Task<string> streamPageRaw(IQueryable<Target> queryable, int pageNumber, int pageSize)
    {
        var stream = new MemoryStream();
        await queryable.StreamPagedJsonArray(stream, pageNumber, pageSize);

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    [Fact]
    public async Task stream_paged_json_array_wire_format_is_camel_case()
    {
        var targets = Target.GenerateRandomData(10).ToArray();
        for (var i = 0; i < targets.Length; i++) targets[i].Number = i;
        await theStore.BulkInsertAsync(targets);

        // Assert against the raw bytes so the client-facing JSON contract is pinned
        // independently of any case-insensitive deserialization on the read side.
        var json = await streamPageRaw(theSession.Query<Target>().OrderBy(x => x.Number), 1, 3);

        json.ShouldContain("\"pageNumber\":");
        json.ShouldContain("\"pageSize\":");
        json.ShouldContain("\"totalItemCount\":");
        json.ShouldContain("\"pageCount\":");
        json.ShouldContain("\"hasNextPage\":");
        json.ShouldContain("\"hasPreviousPage\":");
        json.ShouldContain("\"items\":[");
    }

    [Fact]
    public async Task stream_paged_json_array_page_beyond_end_of_nonempty_set()
    {
        var targets = Target.GenerateRandomData(10).ToArray();
        for (var i = 0; i < targets.Length; i++) targets[i].Number = i;
        await theStore.BulkInsertAsync(targets);

        // Offset 12 > 10 rows: zero rows come back, so count(*) OVER() rides on no row
        // and the envelope reports totalItemCount 0 even though items exist. This documents
        // the window-function limitation (same quirk as PagedList) as an intentional contract.
        var envelope = await streamPage(theSession.Query<Target>().OrderBy(x => x.Number), 5, 3);

        envelope.Items.Length.ShouldBe(0);
        envelope.TotalItemCount.ShouldBe(0);
        envelope.HasNextPage.ShouldBeFalse();
        envelope.HasPreviousPage.ShouldBeTrue();
    }

    [Fact]
    public async Task stream_paged_json_array_total_reflects_filter()
    {
        var targets = Target.GenerateRandomData(20).ToArray();
        for (var i = 0; i < targets.Length; i++) targets[i].Number = i;
        await theStore.BulkInsertAsync(targets);

        // 8 matches (Number 0..7), paged 3 at a time — the total must reflect the filter,
        // not the whole table.
        var envelope = await streamPage(
            theSession.Query<Target>().Where(x => x.Number < 8).OrderBy(x => x.Number), 1, 3);

        envelope.TotalItemCount.ShouldBe(8);
        envelope.PageCount.ShouldBe(3);
        envelope.Items.Select(x => x.Number).ShouldBe(new[] { 0, 1, 2 });
    }

    [Fact]
    public async Task stream_paged_json_array_first_page()
    {
        var targets = Target.GenerateRandomData(10).ToArray();
        for (var i = 0; i < targets.Length; i++) targets[i].Number = i;
        await theStore.BulkInsertAsync(targets);

        var envelope = await streamPage(theSession.Query<Target>().OrderBy(x => x.Number), 1, 3);

        envelope.PageNumber.ShouldBe(1);
        envelope.PageSize.ShouldBe(3);
        envelope.TotalItemCount.ShouldBe(10);
        envelope.PageCount.ShouldBe(4);
        envelope.HasNextPage.ShouldBeTrue();
        envelope.HasPreviousPage.ShouldBeFalse();
        envelope.Items.Length.ShouldBe(3);
        envelope.Items.Select(x => x.Number).ShouldBe(new[] { 0, 1, 2 });
    }

    [Fact]
    public async Task stream_paged_json_array_middle_page()
    {
        var targets = Target.GenerateRandomData(10).ToArray();
        for (var i = 0; i < targets.Length; i++) targets[i].Number = i;
        await theStore.BulkInsertAsync(targets);

        var envelope = await streamPage(theSession.Query<Target>().OrderBy(x => x.Number), 2, 3);

        envelope.PageNumber.ShouldBe(2);
        envelope.PageSize.ShouldBe(3);
        envelope.TotalItemCount.ShouldBe(10);
        envelope.PageCount.ShouldBe(4);
        envelope.HasNextPage.ShouldBeTrue();
        envelope.HasPreviousPage.ShouldBeTrue();
        envelope.Items.Length.ShouldBe(3);
        envelope.Items.Select(x => x.Number).ShouldBe(new[] { 3, 4, 5 });
    }

    [Fact]
    public async Task stream_paged_json_array_last_partial_page()
    {
        var targets = Target.GenerateRandomData(10).ToArray();
        for (var i = 0; i < targets.Length; i++) targets[i].Number = i;
        await theStore.BulkInsertAsync(targets);

        var envelope = await streamPage(theSession.Query<Target>().OrderBy(x => x.Number), 4, 3);

        envelope.PageNumber.ShouldBe(4);
        envelope.PageSize.ShouldBe(3);
        envelope.TotalItemCount.ShouldBe(10);
        envelope.PageCount.ShouldBe(4);
        envelope.HasNextPage.ShouldBeFalse();
        envelope.HasPreviousPage.ShouldBeTrue();
        envelope.Items.Length.ShouldBe(1);
        envelope.Items.Select(x => x.Number).ShouldBe(new[] { 9 });
    }

    [Fact]
    public async Task stream_paged_json_array_single_page()
    {
        var targets = Target.GenerateRandomData(5).ToArray();
        await theStore.BulkInsertAsync(targets);

        var envelope = await streamPage(theSession.Query<Target>(), 1, 25);

        envelope.PageNumber.ShouldBe(1);
        envelope.PageSize.ShouldBe(25);
        envelope.TotalItemCount.ShouldBe(5);
        envelope.PageCount.ShouldBe(1);
        envelope.HasNextPage.ShouldBeFalse();
        envelope.HasPreviousPage.ShouldBeFalse();
        envelope.Items.Length.ShouldBe(5);
    }

    [Fact]
    public async Task stream_paged_json_array_no_hits()
    {
        var envelope = await streamPage(theSession.Query<Target>().Where(x => x.Id == Guid.NewGuid()), 1, 10);

        envelope.PageNumber.ShouldBe(1);
        envelope.PageSize.ShouldBe(10);
        envelope.TotalItemCount.ShouldBe(0);
        envelope.PageCount.ShouldBe(0);
        envelope.HasNextPage.ShouldBeFalse();
        envelope.HasPreviousPage.ShouldBeFalse();
        envelope.Items.ShouldNotBeNull();
        envelope.Items.Length.ShouldBe(0);
    }

    [Fact]
    public async Task stream_paged_json_array_no_hits_beyond_first_page_has_previous_page()
    {
        var envelope = await streamPage(theSession.Query<Target>().Where(x => x.Id == Guid.NewGuid()), 3, 10);

        envelope.TotalItemCount.ShouldBe(0);
        envelope.HasPreviousPage.ShouldBeTrue();
        envelope.HasNextPage.ShouldBeFalse();
    }

    [Fact]
    public async Task stream_paged_json_array_throws_for_page_number_below_one()
    {
        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () =>
        {
            var stream = new MemoryStream();
            await theSession.Query<Target>().StreamPagedJsonArray(stream, 0, 10);
        });
    }

    [Fact]
    public async Task stream_paged_json_array_throws_for_page_size_below_one()
    {
        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () =>
        {
            var stream = new MemoryStream();
            await theSession.Query<Target>().StreamPagedJsonArray(stream, 1, 0);
        });
    }
}
