using System;

namespace Marten.Schema.Testing.Documents
{
    public class Company
    {
        public Company()
        {
            Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }

        public string Name { get; set; }
    }
}