#nullable enable
using System;
using System.Linq;
using System.Reflection;
using JasperFx.Core;
using JasperFx.Core.Reflection;

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

    /// <summary>
    ///     Resolves a method that Marten reaches <b>only</b> reflectively and then <b>invokes</b>, failing
    ///     immediately and by name rather than handing a null <see cref="MethodInfo" /> onwards.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         #5619. <see cref="Type.GetMethod(string)" /> and friends return <c>null</c> for a member that
    ///         is absent — renamed by a refactor, or removed by the trimmer — and the house style was
    ///         <c>GetMethod(…)!</c>, whose <c>!</c> is erased at runtime. So the null flowed on and surfaced
    ///         later as a bare <see cref="NullReferenceException" />, or as
    ///         <c>MakeGenericMethod</c> throwing on a null receiver: an exception naming neither the type nor
    ///         the member it failed to find. That pattern produced three separate-looking LINQ bug reports in
    ///         a sibling store that turned out to be one line (JasperFx/polecat#741).
    ///     </para>
    ///     <para>
    ///         Use this <b>only</b> where a missing method means Marten is broken. It is deliberately NOT for
    ///         the method-call parsers that merely <i>compare</i> against a user-callable extension method:
    ///         there, a trimmed-away method is correct — the application never called it, so no expression
    ///         tree can reference it — and throwing would fail an app for not using a feature. Those sites
    ///         are covered by a convention test instead. See #5619.
    ///     </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The method could not be found.</exception>
    public static MethodInfo RequireMethod(this Type type, string name, BindingFlags flags,
        Type[]? parameterTypes = null)
    {
        var method = parameterTypes == null
            ? type.GetMethod(name, flags)
            : type.GetMethod(name, flags, null, parameterTypes, null);

        if (method != null) return method;

        var signature = parameterTypes == null
            ? name
            : $"{name}({parameterTypes.Select(x => x.NameInCode()).Join(", ")})";

        throw new InvalidOperationException(
            $"Marten could not find the method '{signature}' on {type.FullNameInCode()}, which it reaches "
            + "reflectively and has no fallback for. Either the member was renamed or its signature changed "
            + "without this lookup being updated, or a trimmer removed it — if you publish trimmed or Native "
            + "AOT, see https://martendb.io/configuration/aot-publishing. Please report this at "
            + "https://github.com/JasperFx/marten/issues.");
    }
}
