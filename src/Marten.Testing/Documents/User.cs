using System;
using FubuCore;

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

        public string FullName
        {
            get { return "{0} {1}".ToFormat(FirstName, LastName); }
        }

        public Guid Id { get; set; }
    }
}