using Marten.Schema;

namespace Marten
{
    /// <summary>
    /// Represents a pluggable configuration convention for all persisted documents
    /// </summary>
    public interface IDocumentPolicy
    {
        void Apply(DocumentMapping mapping);
    }
}
