#nullable enable
using System.Linq;
using System.Reflection;
using JasperFx.Core;

namespace Marten.Util;

internal static class ReflectionExtensions
{
    public static string ToTableAlias(this MemberInfo[] members)
    {
        return members.Select(x => x.ToTableAlias()).Join("_");
    }

    public static string ToTableAlias(this MemberInfo member)
    {
        return member.Name.ToTableAlias();
    }
}
