using System;
using System.Reflection;
#nullable enable
namespace Marten.Schema
{
    /// <summary>
    /// Direct Marten to copy the last modified metadata to this member
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class LastModifiedMetadataAttribute: MartenAttribute
    {
        public override void Modify(DocumentMapping mapping, MemberInfo member)
        {
            mapping.Metadata.LastModified.Member = member;
        }
    }

    /// <summary>
    /// Direct Marten to copy the tenant id metadata to this member
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class TenantIdMetadataAttribute: MartenAttribute
    {
        public override void Modify(DocumentMapping mapping, MemberInfo member)
        {
            mapping.Metadata.TenantId.Member = member;
        }
    }

    /// <summary>
    /// Direct Marten to copy the is soft deleted metadata to this member
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class IsSoftDeletedMetadataAttributeAttribute: MartenAttribute
    {
        public override void Modify(DocumentMapping mapping, MemberInfo member)
        {
            mapping.Metadata.IsSoftDeleted.Member = member;
        }
    }

    /// <summary>
    /// Direct Marten to copy the soft deleted timestamp metadata to this member
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class SoftDeletedAtMetadataAttribute: MartenAttribute
    {
        public override void Modify(DocumentMapping mapping, MemberInfo member)
        {
            mapping.Metadata.SoftDeletedAt.Member = member;
        }
    }

    /// <summary>
    /// Direct Marten to copy the hierarchical document type metadata to this member
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class DocumentTypeMetadataAttribute: MartenAttribute
    {
        public override void Modify(DocumentMapping mapping, MemberInfo member)
        {
            mapping.Metadata.DocumentType.Member = member;
        }
    }

}
