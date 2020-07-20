using System;
using System.Collections.Generic;
using Marten.Schema;

namespace Marten.Internal.CodeGeneration
{
    public abstract class DocumentSelectorWithVersions<T, TId> : IDocumentSelector
    {
        protected readonly DocumentMapping _mapping;
        protected readonly ISerializer _serializer;
        protected readonly Dictionary<TId, Guid> _versions;

        public DocumentSelectorWithVersions(IMartenSession session, DocumentMapping mapping)
        {
            _mapping = mapping;
            _serializer = session.Serializer;
            _versions = session.Versions.ForType<T, TId>();
        }
    }
}
