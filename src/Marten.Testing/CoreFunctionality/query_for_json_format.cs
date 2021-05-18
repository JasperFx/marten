using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Marten.Testing.Linq;
using Shouldly;
using Xunit;

namespace Marten.Testing.CoreFunctionality
{

    public class query_for_json_format : IntegrationContext
    {

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
            await theSession.SaveChangesAsync();

            var listJson = await theSession.Query<SimpleUser>().Where(x=>x.Number>=5).ToJsonArrayAsync();
            listJson.ShouldBeSemanticallySameJsonAs($@"[{user1.ToJson()},{user2.ToJson()}]");
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
            userJson.ShouldBeSemanticallySameJsonAs($@"{user1.ToJson()}");
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
            userJson.ShouldBeSemanticallySameJsonAs($@"{user0.ToJson()}");
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
            await theSession.SaveChangesAsync();

            var userJson = await theSession.Query<SimpleUser>().Where(x => x.Number == 5).AsJson().FirstAsync();
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

            var userJson = await theSession.Query<SimpleUser>().AsJson().FirstAsync();
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
                theSession.Query<SimpleUser>().Where(x => x.Number != 5).AsJson().FirstAsync());
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
            userJson.ShouldBeSemanticallySameJsonAs($@"{user1.ToJson()}");
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

            var userJson = await theSession.Query<SimpleUser>().Where(x => x.Number == 5).AsJson().FirstOrDefaultAsync();
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

            var userJson = await theSession.Query<SimpleUser>().AsJson().FirstOrDefaultAsync();
            userJson.ShouldBeSemanticallySameJsonAs($@"{user0.ToJson()}");
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

            var userJson = await theSession.Query<SimpleUser>().Where(x=>x.Number != 5).AsJson().FirstOrDefaultAsync();
            SpecificationExtensions.ShouldBeNull(userJson);
        }

        public query_for_json_format(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
