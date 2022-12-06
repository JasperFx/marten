#nullable enable
using System;
using System.Reflection;
using JasperFx.Core.Reflection;
using Marten.Util;

namespace Marten.Schema;

/// <summary>
///     Direct Marten to make a field or property on a document be
///     set and tracked as the document version or as the aggregate version
///     when used on a projected aggregate
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class VersionAttribute: MartenAttribute
{
    public override void Modify(DocumentMapping mapping, MemberInfo member)
    {
        var memberType = member.GetMemberType();
        if (memberType != typeof(Guid) && memberType != typeof(Guid?))
        {
            if (memberType == typeof(int) || memberType == typeof(long))
            {
                return;
            }

            throw new ArgumentOutOfRangeException(nameof(member),
                "The [Version] attribute is only valid on properties or fields of type Guid/Guid for optimistic concurrency, or int/long for projected aggregate version");
        }

        mapping.UseOptimisticConcurrency = true;
        mapping.Metadata.Version.Member = member;
    }
}
