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
public class VersionAttribute: MartenAttribute, IVersionAttribute
{
    public override void Modify(DocumentMapping mapping, MemberInfo member)
    {
        var memberType = member.GetMemberType();
        if (memberType != typeof(Guid) && memberType != typeof(Guid?))
        {
            if (memberType == typeof(long))
            {
                mapping.UseNumericRevisions = true;
                mapping.Metadata.Revision.Enabled = true;
                mapping.Metadata.Revision.Member = member;

                mapping.Metadata.Version.Enabled = false;
                return;
            }

            if (memberType == typeof(int))
            {
                throw new ArgumentOutOfRangeException(nameof(member),
                    $"The [Version] attribute on {member.DeclaringType?.FullName ?? "(unknown)"}.{member.Name} is annotated on an int. As of Marten 9, numeric document revisions are widened to long. Change the property type to long. See https://martendb.io/migration-guide.html#numeric-document-revisions-widened-from-int-to-long");
            }

            throw new ArgumentOutOfRangeException(nameof(member),
                "The [Version] attribute is only valid on properties or fields of type Guid/Guid? for optimistic concurrency, or long for projected aggregate version");
        }

        mapping.UseOptimisticConcurrency = true;
        mapping.Metadata.Version.Member = member;
    }
}
