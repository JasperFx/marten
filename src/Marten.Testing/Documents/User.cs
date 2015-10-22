using System;
using FubuCore;

namespace Marten.Testing.Documents
{
    // The IDocument interface is just a temporary crutch
    // for now. It won't be necessary in the end
    public class User : IDocument
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