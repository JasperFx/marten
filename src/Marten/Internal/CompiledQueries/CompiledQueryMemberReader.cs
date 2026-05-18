#nullable enable
using System;
using System.Reflection;

namespace Marten.Internal.CompiledQueries;

/// <summary>
/// Tiny reflection bridge used by <see cref="ICompiledQueryAwareFilter"/>
/// runtime setters to read the value of a captured <see cref="MemberInfo"/>
/// from a user's compiled-query instance. Handles the two member kinds Marten
/// matches against — <see cref="PropertyInfo"/> and <see cref="FieldInfo"/> —
/// and surfaces a clear exception for anything else.
/// </summary>
/// <remarks>
/// AOT-clean: pure reflection over the user-supplied <see cref="MemberInfo"/>,
/// no Expression compile / no <c>MakeGenericMethod</c>. Called once per
/// compiled-query invocation (not per row) so the reflection cost is
/// negligible.
/// </remarks>
internal static class CompiledQueryMemberReader
{
    public static object? Read(MemberInfo member, object instance)
    {
        return member switch
        {
            PropertyInfo p => p.GetValue(instance),
            FieldInfo f => f.GetValue(instance),
            _ => throw new InvalidOperationException(
                $"Unable to read compiled-query parameter from {member.DeclaringType?.FullName}.{member.Name}: " +
                $"member kind {member.MemberType} is not supported (expected Property or Field).")
        };
    }
}
