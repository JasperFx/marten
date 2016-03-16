using System;
using System.Linq;
using Marten.Linq;
using Marten.Services;
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
    }

    public class query_for_json_format : DocumentSessionFixture<NulloIdentityMap>
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

            var listJson = theSession.Query<SimpleUser>().Where(x=>x.Number>=5).ToListJson();
            listJson.ShouldBe($@"[
{{
""Id"": ""{user1.Id}"", ""Number"": {user1.Number}, ""Address"": 
{{
""Street"": ""{user1.Address.Street}"", ""HouseNumber"": ""{user1.Address.HouseNumber}""
}}, 
""UserName"": ""{user1.UserName}"", 
""Birthdate"": ""{user1.Birthdate.ToString("s")}""
}},
{{
""Id"": ""{user2.Id}"", ""Number"": {user2.Number}, ""Address"": 
{{
""Street"": ""{user2.Address.Street}"", ""HouseNumber"": ""{user2.Address.HouseNumber}""
}}, 
""UserName"": ""{user2.UserName}"", 
""Birthdate"": ""{user2.Birthdate.ToString("s")}""
}}
]".Replace("\r\n",""));
        }

        [Fact]
        public void to_list_async()
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

            var listJson = theSession.Query<SimpleUser>().Where(x=>x.Number>=5).ToListJsonAsync().GetAwaiter().GetResult();
            listJson.ShouldBe($@"[
{{
""Id"": ""{user1.Id}"", ""Number"": {user1.Number}, ""Address"": 
{{
""Street"": ""{user1.Address.Street}"", ""HouseNumber"": ""{user1.Address.HouseNumber}""
}}, 
""UserName"": ""{user1.UserName}"", 
""Birthdate"": ""{user1.Birthdate.ToString("s")}""
}},
{{
""Id"": ""{user2.Id}"", ""Number"": {user2.Number}, ""Address"": 
{{
""Street"": ""{user2.Address.Street}"", ""HouseNumber"": ""{user2.Address.HouseNumber}""
}}, 
""UserName"": ""{user2.UserName}"", 
""Birthdate"": ""{user2.Birthdate.ToString("s")}""
}}
]".Replace("\r\n",""));
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

            var userJson = theSession.Query<SimpleUser>().FirstJson(x => x.Number == 5);
            userJson.ShouldBe($@"
{{
""Id"": ""{user1.Id}"", ""Number"": {user1.Number}, ""Address"": 
{{
""Street"": ""{user1.Address.Street}"", ""HouseNumber"": ""{user1.Address.HouseNumber}""
}}, 
""UserName"": ""{user1.UserName}"", 
""Birthdate"": ""{user1.Birthdate.ToString("s")}""
}}".Replace("\r\n",""));
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

            var ex = Exception<InvalidOperationException>.ShouldBeThrownBy(()=>theSession.Query<SimpleUser>().FirstJson(x=>x.Number != 5));
            ex.Message.ShouldBe("Sequence contains no elements");
        }

        [Fact]
        public void first_async_returns_first()
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

            var userJson = theSession.Query<SimpleUser>().FirstJsonAsync(x => x.Number == 5).GetAwaiter().GetResult();
            userJson.ShouldBe($@"
{{
""Id"": ""{user1.Id}"", ""Number"": {user1.Number}, ""Address"": 
{{
""Street"": ""{user1.Address.Street}"", ""HouseNumber"": ""{user1.Address.HouseNumber}""
}}, 
""UserName"": ""{user1.UserName}"", 
""Birthdate"": ""{user1.Birthdate.ToString("s")}""
}}".Replace("\r\n",""));
        }

        [Fact]
        public void first_async_throws_when_none_returned()
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

            var ex = Exception<InvalidOperationException>.ShouldBeThrownByAsync(()=>theSession.Query<SimpleUser>().FirstJsonAsync(x=>x.Number != 5)).GetAwaiter().GetResult();
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

            var userJson = theSession.Query<SimpleUser>().FirstOrDefaultJson(x => x.Number == 5);
            userJson.ShouldBe($@"
{{
""Id"": ""{user1.Id}"", ""Number"": {user1.Number}, ""Address"": 
{{
""Street"": ""{user1.Address.Street}"", ""HouseNumber"": ""{user1.Address.HouseNumber}""
}}, 
""UserName"": ""{user1.UserName}"", 
""Birthdate"": ""{user1.Birthdate.ToString("s")}""
}}".Replace("\r\n",""));
        }

        [Fact]
        public void first_or_default_returns_first_async()
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

            var userJson = theSession.Query<SimpleUser>().FirstOrDefaultJsonAsync(x => x.Number == 5).GetAwaiter().GetResult();
            userJson.ShouldBe($@"
{{
""Id"": ""{user1.Id}"", ""Number"": {user1.Number}, ""Address"": 
{{
""Street"": ""{user1.Address.Street}"", ""HouseNumber"": ""{user1.Address.HouseNumber}""
}}, 
""UserName"": ""{user1.UserName}"", 
""Birthdate"": ""{user1.Birthdate.ToString("s")}""
}}".Replace("\r\n",""));
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

            var userJson = theSession.Query<SimpleUser>().FirstOrDefaultJson(x=>x.Number != 5);
            userJson.ShouldBeNull();
        }

        [Fact]
        public void first_or_default_async_returns_default()
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

            var userJson = theSession.Query<SimpleUser>().FirstOrDefaultJsonAsync(x=>x.Number != 5).GetAwaiter().GetResult();
            userJson.ShouldBeNull();
        }
    }
}