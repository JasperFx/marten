using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Util;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class Address
    {
        public string HouseNumber { get; set; }
        public string Street { get; set; }
    }
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
        public Address Address { get; set; }

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

    public class query_for_json_format : DocumentSessionFixture<NulloIdentityMap>
    {
        public query_for_json_format()
        {
            // These tests are hard-coded for the Json that Newtonsoft puts out
            StoreOptions(_ => _.Serializer(new JsonNetSerializer()));
        }

        [Fact]
        public void to_list()
        {
            var user0 = new SimpleUser
            {
                UserName = "Invisible man",
                Number = 4,
                Birthdate = new DateTime(1986, 10, 4),
                Address = new Address {HouseNumber = "0", Street = "rue de l'invisible"}
            };
            var user1 = new SimpleUser
            {
                UserName = "Mr Fouine",
                Number = 5,
                Birthdate = new DateTime(1986, 10, 4),
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            var user2 = new SimpleUser
            {
                UserName = "Mrs Fouine",
                Number = 6,
                Birthdate = new DateTime(1987, 10, 4),
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            theSession.Store(user0,user1,user2);
            theSession.SaveChanges();

            var listJson = theSession.Query<SimpleUser>().Where(x=>x.Number>=5).ToJsonArray();
            listJson.ShouldBe($@"[{user1.ToJson().CaseBy(theStore.Serializer.Casing)},{user2.ToJson().CaseBy(theStore.Serializer.Casing)}]");
        }

        [Fact]
        public async Task to_list_async()
        {
            var user0 = new SimpleUser
            {
                UserName = "Invisible man",
                Number = 4,
                Birthdate = new DateTime(1986, 10, 4),
                Address = new Address {HouseNumber = "0", Street = "rue de l'invisible"}
            };
            var user1 = new SimpleUser
            {
                UserName = "Mr Fouine",
                Number = 5,
                Birthdate = new DateTime(1986, 10, 4),
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            var user2 = new SimpleUser
            {
                UserName = "Mrs Fouine",
                Number = 6,
                Birthdate = new DateTime(1987, 10, 4),
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            theSession.Store(user0,user1,user2);
            theSession.SaveChanges();

            var listJson = await theSession.Query<SimpleUser>().Where(x=>x.Number>=5).ToJsonArrayAsync().ConfigureAwait(false);
            listJson.ShouldBe($@"[{user1.ToJson().CaseBy(theStore.Serializer.Casing)},{user2.ToJson().CaseBy(theStore.Serializer.Casing)}]");
        }

        [Fact]
        public void first_returns_first()
        {
            var user0 = new SimpleUser
            {
                UserName = "Invisible man",
                Number = 4,
                Birthdate = new DateTime(1986, 10, 4),
                Address = new Address {HouseNumber = "0", Street = "rue de l'invisible"}
            };
            var user1 = new SimpleUser
            {
                UserName = "Mr Fouine",
                Number = 5,
                Birthdate = new DateTime(1986, 10, 4),
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            var user2 = new SimpleUser
            {
                UserName = "Mrs Fouine",
                Number = 5,
                Birthdate = new DateTime(1987, 10, 4),
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            theSession.Store(user0,user1,user2);
            theSession.SaveChanges();

            var userJson = theSession.Query<SimpleUser>().Where(x => x.Number == 5).AsJson().First();
            userJson.ShouldBe($@"{user1.ToJson()}".CaseBy(theStore.Serializer.Casing));
        }

        [Fact]
        public void first_returns_first_line()
        {
            var user0 = new SimpleUser
            {
                UserName = "Invisible man",
                Number = 4,
                Birthdate = new DateTime(1986, 10, 4),
                Address = new Address {HouseNumber = "0", Street = "rue de l'invisible"}
            };
            var user1 = new SimpleUser
            {
                UserName = "Mr Fouine",
                Number = 5,
                Birthdate = new DateTime(1986, 10, 4),
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            var user2 = new SimpleUser
            {
                UserName = "Mrs Fouine",
                Number = 5,
                Birthdate = new DateTime(1987, 10, 4),
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            theSession.Store(user0,user1,user2);
            theSession.SaveChanges();

            var userJson = theSession.Query<SimpleUser>().AsJson().First();
            userJson.ShouldBe($@"{user0.ToJson()}".CaseBy(theStore.Serializer.Casing));
        }

        [Fact]
        public void first_throws_when_none_returned()
        {
            var user1 = new SimpleUser
            {
                UserName = "Mr Fouine",
                Number = 5,
                Birthdate = new DateTime(1986, 10, 4),
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            var user2 = new SimpleUser
            {
                UserName = "Mrs Fouine",
                Number = 5,
                Birthdate = new DateTime(1987, 10, 4),
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            theSession.Store(user1,user2);
            theSession.SaveChanges();
            
            var ex = Exception<InvalidOperationException>.ShouldBeThrownBy(() => theSession.Query<SimpleUser>().Where(x => x.Number != 5).AsJson().First());
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
                Address = new Address {HouseNumber = "0", Street = "rue de l'invisible"}
            };
            var user1 = new SimpleUser
            {
                UserName = "Mr Fouine",
                Number = 5,
                Birthdate = new DateTime(1986, 10, 4),
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            var user2 = new SimpleUser
            {
                UserName = "Mrs Fouine",
                Number = 5,
                Birthdate = new DateTime(1987, 10, 4),
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            theSession.Store(user0,user1,user2);
            theSession.SaveChanges();

            var userJson = await theSession.Query<SimpleUser>().Where(x => x.Number == 5).AsJson().FirstAsync().ConfigureAwait(false);
            userJson.ShouldBe($@"{user1.ToJson()}".CaseBy(theStore.Serializer.Casing));
        }

        [Fact]
        public async Task first_async_returns_first_line()
        {
            var user0 = new SimpleUser
            {
                UserName = "Invisible man",
                Number = 4,
                Birthdate = new DateTime(1986, 10, 4),
                Address = new Address {HouseNumber = "0", Street = "rue de l'invisible"}
            };
            var user1 = new SimpleUser
            {
                UserName = "Mr Fouine",
                Number = 5,
                Birthdate = new DateTime(1986, 10, 4),
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            var user2 = new SimpleUser
            {
                UserName = "Mrs Fouine",
                Number = 5,
                Birthdate = new DateTime(1987, 10, 4),
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            theSession.Store(user0,user1,user2);
            theSession.SaveChanges();

            var userJson = await theSession.Query<SimpleUser>().AsJson().FirstAsync().ConfigureAwait(false);
            userJson.ShouldBe($@"{user0.ToJson()}".CaseBy(theStore.Serializer.Casing));
        }

        [Fact]
        public async Task first_async_throws_when_none_returned()
        {
            var user1 = new SimpleUser
            {
                UserName = "Mr Fouine",
                Number = 5,
                Birthdate = new DateTime(1986, 10, 4),
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            var user2 = new SimpleUser
            {
                UserName = "Mrs Fouine",
                Number = 5,
                Birthdate = new DateTime(1987, 10, 4),
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            theSession.Store(user1,user2);
            theSession.SaveChanges();
            
            var ex = await Exception<InvalidOperationException>.ShouldBeThrownByAsync(() => 
                theSession.Query<SimpleUser>().Where(x => x.Number != 5).AsJson().FirstAsync()).ConfigureAwait(false);
            ex.Message.ShouldBe("Sequence contains no elements"); 
        }

        [Fact]
        public void first_or_default_returns_first()
        {
            var user0 = new SimpleUser
            {
                UserName = "Invisible man",
                Number = 4,
                Birthdate = new DateTime(1986, 10, 4),
                Address = new Address {HouseNumber = "0", Street = "rue de l'invisible"}
            };
            var user1 = new SimpleUser
            {
                UserName = "Mr Fouine",
                Number = 5,
                Birthdate = new DateTime(1986, 10, 4),
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            var user2 = new SimpleUser
            {
                UserName = "Mrs Fouine",
                Number = 5,
                Birthdate = new DateTime(1987, 10, 4),
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            theSession.Store(user0,user1,user2);
            theSession.SaveChanges();

            var userJson = theSession.Query<SimpleUser>().Where(x => x.Number == 5).AsJson().FirstOrDefault();
            userJson.ShouldBe($@"{user1.ToJson()}".CaseBy(theStore.Serializer.Casing));
        }

        [Fact]
        public void first_or_default_returns_first_line()
        {
            var user0 = new SimpleUser
            {
                UserName = "Invisible man",
                Number = 4,
                Birthdate = new DateTime(1986, 10, 4),
                Address = new Address {HouseNumber = "0", Street = "rue de l'invisible"}
            };
            var user1 = new SimpleUser
            {
                UserName = "Mr Fouine",
                Number = 5,
                Birthdate = new DateTime(1986, 10, 4),
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            var user2 = new SimpleUser
            {
                UserName = "Mrs Fouine",
                Number = 5,
                Birthdate = new DateTime(1987, 10, 4),
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            theSession.Store(user0,user1,user2);
            theSession.SaveChanges();

            var userJson = theSession.Query<SimpleUser>().AsJson().FirstOrDefault();
            userJson.ShouldBe($@"{user0.ToJson()}".CaseBy(theStore.Serializer.Casing));
        }

        [Fact]
        public async Task first_or_default_returns_first_async()
        {
            var user0 = new SimpleUser
            {
                UserName = "Invisible man",
                Number = 4,
                Birthdate = new DateTime(1986, 10, 4),
                Address = new Address {HouseNumber = "0", Street = "rue de l'invisible"}
            };
            var user1 = new SimpleUser
            {
                UserName = "Mr Fouine",
                Number = 5,
                Birthdate = new DateTime(1986, 10, 4),
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            var user2 = new SimpleUser
            {
                UserName = "Mrs Fouine",
                Number = 5,
                Birthdate = new DateTime(1987, 10, 4),
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            theSession.Store(user0,user1,user2);
            theSession.SaveChanges();

            var userJson = await theSession.Query<SimpleUser>().Where(x => x.Number == 5).AsJson().FirstOrDefaultAsync().ConfigureAwait(false);
            userJson.ShouldBe($@"{user1.ToJson()}".CaseBy(theStore.Serializer.Casing));
        }

        [Fact]
        public async Task first_or_default_returns_first_line_async()
        {
            var user0 = new SimpleUser
            {
                UserName = "Invisible man",
                Number = 4,
                Birthdate = new DateTime(1986, 10, 4),
                Address = new Address {HouseNumber = "0", Street = "rue de l'invisible"}
            };
            var user1 = new SimpleUser
            {
                UserName = "Mr Fouine",
                Number = 5,
                Birthdate = new DateTime(1986, 10, 4),
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            var user2 = new SimpleUser
            {
                UserName = "Mrs Fouine",
                Number = 5,
                Birthdate = new DateTime(1987, 10, 4),
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            theSession.Store(user0,user1,user2);
            theSession.SaveChanges();

            var userJson = await theSession.Query<SimpleUser>().AsJson().FirstOrDefaultAsync().ConfigureAwait(false);
            userJson.ShouldBe($@"{user0.ToJson()}".CaseBy(theStore.Serializer.Casing));
        }

        [Fact]
        public void first_or_default_returns_default()
        {
            var user1 = new SimpleUser
            {
                UserName = "Mr Fouine",
                Number = 5,
                Birthdate = new DateTime(1986, 10, 4),
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            var user2 = new SimpleUser
            {
                UserName = "Mrs Fouine",
                Number = 5,
                Birthdate = new DateTime(1987, 10, 4),
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            theSession.Store(user1,user2);
            theSession.SaveChanges();

            var userJson = theSession.Query<SimpleUser>().Where(x=>x.Number != 5).AsJson().FirstOrDefault();
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
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            var user2 = new SimpleUser
            {
                UserName = "Mrs Fouine",
                Number = 5,
                Birthdate = new DateTime(1987, 10, 4),
                Address = new Address {HouseNumber = "12bis", Street = "rue de la martre"}
            };
            theSession.Store(user1,user2);
            theSession.SaveChanges();

            var userJson = await theSession.Query<SimpleUser>().Where(x=>x.Number != 5).AsJson().FirstOrDefaultAsync().ConfigureAwait(false);
            userJson.ShouldBeNull();
        }
    }
}