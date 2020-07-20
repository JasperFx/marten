using Marten.Schema;

namespace Marten.Internal.CodeGeneration
{
    public abstract class DocumentSelectorWithOnlySerializer : IDocumentSelector
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
