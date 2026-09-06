#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Marten.Internal.CompiledQueries;

internal interface IParameterFinder
{
    bool Matches(Type memberType);
    bool AreValuesUnique(object query, CompiledQueryPlan plan);
    Queue<object> UniqueValueQueue(Type type);

    /// <summary>
    ///     Build the <see cref="IQueryMember" /> for a compiled query member of
    ///     <paramref name="memberType" />, or null if this finder does not own that type.
    /// </summary>
    /// <remarks>
    ///     #5328: <c>PropertyQueryMember&lt;T&gt;</c> / <c>FieldQueryMember&lt;T&gt;</c> used to be
    ///     closed with <c>MakeGenericType</c> + <c>Activator.CreateInstance</c>, which Native AOT
    ///     cannot satisfy — the first compiled query threw
    ///     <c>MissingMethodException: No parameterless constructor defined for type
    ///     'PropertyQueryMember`1[System.String]'</c>. The finders already know their own closed
    ///     type at compile time (they are constructed as <c>SimpleParameterFinder&lt;string&gt;</c>,
    ///     <c>ArrayParameterFinder&lt;Guid&gt;</c>, ... in <see cref="QueryCompiler" />'s static
    ///     constructor), so letting each finder build its own member keeps every instantiation
    ///     statically reachable.
    /// </remarks>
    IQueryMember? BuildQueryMember(MemberInfo member, Type memberType);
}
