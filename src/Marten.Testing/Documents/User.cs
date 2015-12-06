using System;
using Baseline;
using Marten.Testing.Fixtures;

namespace Marten.Testing.Documents
{
    public class User
    {
        public User()
        {
            Id = Guid.NewGuid();
        }

        public string FirstName { get; set; }
        public string LastName { get; set; }

        public bool Internal { get; set; }

        public string FullName
        {
            get { return "{0} {1}".ToFormat(FirstName, LastName); }
        }

        public Address Address { get; set; }

        public Guid Id { get; set; }
        public int Age { get; set; }
    }
}