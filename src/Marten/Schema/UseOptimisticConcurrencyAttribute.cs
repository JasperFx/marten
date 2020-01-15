using System;

namespace Marten.Schema
{
    /// <summary>
    /// Directs Marten to use optimistic versioning checks when updating this document type
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class UseOptimisticConcurrencyAttribute: MartenAttribute
    {
        public override void Modify(DocumentMapping mapping)
        {
            mapping.UseOptimisticConcurrency = true;
        }
    }
}
