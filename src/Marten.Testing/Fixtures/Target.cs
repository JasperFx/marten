using System;

namespace Marten.Testing.Fixtures
{
    public class Target : IDocument
    {
        public Target()
        {
            Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }

        public int Number { get; set; }
        public long Long { get; set; }
        public string String { get; set; }
    }
}