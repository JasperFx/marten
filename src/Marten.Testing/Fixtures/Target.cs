using System;
using System.Collections.Generic;
using FubuCore;

namespace Marten.Testing.Fixtures
{
    public class Target
    {
        public Target()
        {
            Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }

        public int Number { get; set; }
        public long Long { get; set; }
        public string String { get; set; }

        public Target Inner { get; set; }
    }

    public class Address
    {
        public Address()
        {
        }

        public Address(string text)
        {
            var parts = text.ToDelimitedArray();
            Address1 = parts[0];
            City = parts[1];
            StateOrProvince = parts[2];
        }

        public string Address1 { get; set; }
        public string Address2 { get; set; }
        public string City { get; set; }
        public string StateOrProvince { get; set; }
        public string Country { get; set; }
        public string PostalCode { get; set; }
    }
}