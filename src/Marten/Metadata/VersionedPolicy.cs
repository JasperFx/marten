using Baseline;
using Marten.Schema;
#nullable enable
namespace Marten.Metadata
{
    internal class VersionedPolicy: IDocumentPolicy
    {
        public void Apply(DocumentMapping mapping)
        {
            if (mapping.DocumentType.CanBeCastTo<IVersioned>())
            {
                mapping.UseOptimisticConcurrency = true;
                mapping.Metadata.Version.Enabled = true;
                mapping.Metadata.Version.Member = mapping.DocumentType.GetProperty(nameof(IVersioned.Version));
            }
        }
    }
}
