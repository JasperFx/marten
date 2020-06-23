using Marten.Schema;

namespace Marten.V4Internals
{
    public abstract class DocumentSelectorWithOnlySerializer
    {
        protected readonly DocumentMapping _mapping;
        protected readonly ISerializer _serializer;

        public DocumentSelectorWithOnlySerializer(IMartenSession session, DocumentMapping mapping)
        {
            _mapping = mapping;
            _serializer = session.Serializer;
        }
    }
}
