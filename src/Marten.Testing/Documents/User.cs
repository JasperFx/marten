using System;

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
    }
}