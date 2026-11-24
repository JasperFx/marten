#nullable enable
using System;
using System.Reflection;

namespace Marten.Schema;

/// <summary>
///     Direct Marten to copy the last modified metadata to this member
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
///     Direct Marten to copy the created timestamp metadata to this member
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class CreatedAtMetadataAttribute: MartenAttribute
{
    public override void Modify(DocumentMapping mapping, MemberInfo member)
    {
        mapping.Metadata.CreatedAt.Member = member;
    }
}

/// <summary>
///     Direct Marten to copy the tenant id metadata to this member
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
///     Direct Marten to copy the is soft deleted metadata to this member. The class name carries a doubled
///     <c>Attribute</c> suffix, so it is written <c>[IsSoftDeletedMetadataAttribute]</c>; prefer
///     <see cref="SoftDeletedMetadataAttribute" />, written <c>[SoftDeletedMetadata]</c>.
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
///     Direct Marten to copy the is soft deleted metadata (<c>mt_deleted</c>) to this member. The correctly named
///     form of <see cref="IsSoftDeletedMetadataAttributeAttribute" /> (#6194). It cannot be named
///     <c>IsSoftDeletedMetadataAttribute</c>: beside the existing class, that would make every
///     <c>[IsSoftDeletedMetadataAttribute]</c> already written ambiguous (CS1614).
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class SoftDeletedMetadataAttribute: IsSoftDeletedMetadataAttributeAttribute
{
}

/// <summary>
///     Direct Marten to copy the soft deleted timestamp metadata to this member
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
///     Direct Marten to copy the hierarchical document type metadata to this member
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class DocumentTypeMetadataAttribute: MartenAttribute
{
    public override void Modify(DocumentMapping mapping, MemberInfo member)
    {
        mapping.Metadata.DocumentType.Member = member;
    }
}
