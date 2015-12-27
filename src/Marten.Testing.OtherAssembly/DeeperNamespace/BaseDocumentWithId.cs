using System;

namespace Marten.Testing.OtherAssembly.DeeperNamespace
{
    public abstract class BaseDocumentWithId
    {
        public BaseDocumentWithId()
        {
            Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }
    }
}
