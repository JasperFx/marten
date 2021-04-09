using System;
using Marten.Storage;
#nullable enable
namespace Marten.Schema
{
    /// <summary>
    /// Directs Marten to store this document type with conjoined multi-tenancy
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class MultiTenantedAttribute: MartenAttribute
    {
        public override void Modify(DocumentMapping mapping)
        {
            mapping.TenancyStyle = TenancyStyle.Conjoined;
        }
    }
}
