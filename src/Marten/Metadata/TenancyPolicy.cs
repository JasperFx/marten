using Baseline;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Metadata
{
    internal class TenancyPolicy: IDocumentPolicy
    {
        public void Apply(DocumentMapping mapping)
        {
            if (mapping.DocumentType.CanBeCastTo<ITenanted>())
            {
                mapping.TenancyStyle = TenancyStyle.Conjoined;
                mapping.Metadata.TenantId.Enabled = true;
                mapping.Metadata.TenantId.Member = mapping.DocumentType.GetProperty(nameof(ITenanted.TenantId));
            }
        }
    }
}
