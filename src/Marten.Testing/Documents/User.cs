using System;
using FubuCore;

namespace Marten.Testing.Documents
{
    public class User : IDocument
    {
        public User()
        {
            Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }

        public string FirstName { get; set; }
        public string LastName { get; set; }

        public string FullName
        {
            get { return "{0} {1}".ToFormat(FirstName, LastName); }
        }
    }
}