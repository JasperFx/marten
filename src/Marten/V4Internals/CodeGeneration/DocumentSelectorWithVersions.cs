using System;
using System.Collections.Generic;
using Marten.Schema;

namespace Marten.V4Internals
{
    public abstract class DocumentSelectorWithVersions<T, TId>
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
