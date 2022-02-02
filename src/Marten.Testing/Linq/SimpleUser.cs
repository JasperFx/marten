using System;
using Marten.Testing.Documents;

namespace Marten.Testing.Linq
{
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
}
