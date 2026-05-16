using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Marten.SourceGenerator;

/// <summary>
/// Roslyn-symbol equivalent of <c>Marten.Internal.CompiledQueries.CompiledQueryPlan.sortMembers</c>.
/// Classifies each public field/property on a compiled-query type into the same
/// categories the runtime planner uses: query parameter, include reader, statistics
/// carrier, or "skip" (nullable / unsupported / decorated with [MartenIgnore]).
/// </summary>
/// <remarks>
/// <para>
/// Keeping this in lockstep with the runtime classifier is critical — a member
/// classified differently by the generator vs. the planner would either produce
/// a binder that misses a real parameter or a binder with a "case" for a member
/// the planner never asks about. Both fail loudly at runtime. The PoC validates
/// that lockstep via the parser fidelity test (iteration 4).
/// </para>
/// </remarks>
internal static class QueryMemberClassifier
{
    internal enum MemberKind
    {
        SimpleParameter,
        EnumParameter,
        ArrayParameter,
        Statistics,
        Include,
        Skip
    }

    /// <summary>
    /// Which Include collection shape an <c>Include</c> member carries. The
    /// generator emits a different <c>Include.ReaderTo*</c> factory call for
    /// each shape, so it needs to know up front.
    /// </summary>
    internal enum IncludeShape
    {
        None,         // not an Include member
        Action,       // Action&lt;T&gt;
        List,         // IList&lt;T&gt; (or a class implementing it)
        Dictionary    // IDictionary&lt;TKey, TValue&gt;
    }

    /// <param name="ElementType">
    /// For <c>ArrayParameter</c>: the array element type.
    /// For <c>Include</c> with <see cref="IncludeShape.Action"/> or
    /// <see cref="IncludeShape.List"/>: the collection element type.
    /// For <c>Include</c> with <see cref="IncludeShape.Dictionary"/>: the
    /// dictionary value type (the include payload).
    /// Otherwise <see langword="null"/>.
    /// </param>
    /// <param name="DictionaryKeyType">
    /// Populated only for <c>Include</c> with <see cref="IncludeShape.Dictionary"/> —
    /// the dictionary key type, which becomes the <c>TId</c> generic argument
    /// to <c>Include.ReaderToDictionary&lt;T, TId&gt;</c>.
    /// </param>
    internal readonly record struct Classified(
        MemberKind Kind,
        string Name,
        ITypeSymbol Type,
        ITypeSymbol? ElementType,
        IncludeShape IncludeShape = IncludeShape.None,
        ITypeSymbol? DictionaryKeyType = null);

    /// <summary>
    /// Iterates the public instance fields + properties on <paramref name="queryType"/>
    /// (and its base types) and yields one <see cref="Classified"/> entry per member.
    /// </summary>
    public static IEnumerable<Classified> Classify(INamedTypeSymbol queryType)
    {
        foreach (var member in EnumerateMembers(queryType))
        {
            if (HasMartenIgnore(member))
            {
                continue;
            }

            var type = GetMemberType(member);
            if (type is null)
            {
                continue;
            }

            yield return Classify(member.Name, type);
        }
    }

    private static Classified Classify(string name, ITypeSymbol type)
    {
        // Statistics carrier. The real type lives in Marten.Linq; the runtime
        // planner matches by typeof() but the generator only has symbol info, so
        // we match by display name. (Marten.Linq.QueryStatistics is the canonical
        // public namespace per src/Marten/Linq/QueryStatistics.cs.)
        if (type.ToDisplayString() == "Marten.Linq.QueryStatistics")
        {
            return new Classified(MemberKind.Statistics, name, type, null);
        }

        // Arrays of supported element types are query parameters, NOT includes — this
        // matches the runtime planner's ordering check (see CompiledQueryPlan.sortMembers).
        if (type is IArrayTypeSymbol arrayType)
        {
            var element = arrayType.ElementType;
            // byte[] is a simple parameter (Bytea), not an array parameter.
            if (element.SpecialType == SpecialType.System_Byte)
            {
                return new Classified(MemberKind.SimpleParameter, name, type, null);
            }

            if (NpgsqlTypeMap.ResolveSimple(element) is not null)
            {
                return new Classified(MemberKind.ArrayParameter, name, type, element);
            }
            // Otherwise an unsupported array element — skip.
            return new Classified(MemberKind.Skip, name, type, null);
        }

        // Include collections / readers — IDictionary<,>, IList<>, Action<>.
        // Pull out the closing generic args so the generator can emit a typed
        // factory call (Include.ReaderToAction<T>, ReaderToList<T>, ReaderToDictionary<T, TId>).
        if (TryGetClosedGenericArgs(type, "System.Collections.Generic.IDictionary<TKey, TValue>", out var dictArgs))
        {
            return new Classified(MemberKind.Include, name, type, dictArgs[1],
                IncludeShape.Dictionary, dictArgs[0]);
        }
        if (TryGetClosedGenericArgs(type, "System.Collections.Generic.IList<T>", out var listArgs))
        {
            return new Classified(MemberKind.Include, name, type, listArgs[0], IncludeShape.List);
        }
        if (TryGetClosedGenericArgs(type, "System.Action<T>", out var actionArgs))
        {
            return new Classified(MemberKind.Include, name, type, actionArgs[0], IncludeShape.Action);
        }

        // Nullable<T> — the runtime planner marks these invalid; we honor that and skip.
        if (IsNullableValueType(type))
        {
            return new Classified(MemberKind.Skip, name, type, null);
        }

        // Enum parameter.
        if (type.TypeKind == TypeKind.Enum)
        {
            return new Classified(MemberKind.EnumParameter, name, type, null);
        }

        // Simple BCL parameter.
        if (NpgsqlTypeMap.ResolveSimple(type) is not null)
        {
            return new Classified(MemberKind.SimpleParameter, name, type, null);
        }

        return new Classified(MemberKind.Skip, name, type, null);
    }

    private static IEnumerable<ISymbol> EnumerateMembers(INamedTypeSymbol type)
    {
        // Mirror MemberInfo.GetFields(BindingFlags.Public | BindingFlags.Instance) followed by
        // MemberInfo.GetProperties(...). Walk the inheritance chain to pick up inherited
        // public members; only public, non-static.
        var current = type;
        var seen = new HashSet<string>();
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            foreach (var f in current.GetMembers().OfType<IFieldSymbol>())
            {
                if (f.IsStatic || f.DeclaredAccessibility != Accessibility.Public) continue;
                if (f.IsImplicitlyDeclared) continue; // backing fields, etc.
                if (seen.Add(f.Name)) yield return f;
            }
            foreach (var p in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (p.IsStatic || p.DeclaredAccessibility != Accessibility.Public) continue;
                if (p.IsIndexer) continue;
                if (seen.Add(p.Name)) yield return p;
            }
            current = current.BaseType;
        }
    }

    private static ITypeSymbol? GetMemberType(ISymbol member) => member switch
    {
        IFieldSymbol f => f.Type,
        IPropertySymbol p => p.Type,
        _ => null
    };

    private static bool HasMartenIgnore(ISymbol member)
    {
        foreach (var attr in member.GetAttributes())
        {
            var attrName = attr.AttributeClass?.ToDisplayString();
            if (attrName == "Marten.Events.CodeGeneration.MartenIgnoreAttribute"
                || attrName == "JasperFx.Core.JasperFxIgnoreAttribute")
            {
                return true;
            }
        }
        return false;
    }

    /// <param name="openTypeDisplayName">
    /// The open-generic display name as Roslyn renders it, e.g.
    /// <c>"System.Collections.Generic.IList&lt;T&gt;"</c>.
    /// </param>
    private static bool ClosesGeneric(ITypeSymbol type, string openTypeDisplayName)
        => TryGetClosedGenericArgs(type, openTypeDisplayName, out _);

    /// <summary>
    /// Like <see cref="ClosesGeneric"/> but also produces the closing generic
    /// arguments when a match is found. Returns the args from the concrete type
    /// itself if it directly is the open generic, otherwise from the matching
    /// interface in <see cref="ITypeSymbol.AllInterfaces"/>.
    /// </summary>
    private static bool TryGetClosedGenericArgs(
        ITypeSymbol type, string openTypeDisplayName,
        out System.Collections.Immutable.ImmutableArray<ITypeSymbol> args)
    {
        if (type is INamedTypeSymbol nt && nt.IsGenericType
            && nt.OriginalDefinition.ToDisplayString() == openTypeDisplayName)
        {
            args = nt.TypeArguments;
            return true;
        }
        foreach (var iface in type.AllInterfaces)
        {
            if (iface.IsGenericType
                && iface.OriginalDefinition.ToDisplayString() == openTypeDisplayName)
            {
                args = iface.TypeArguments;
                return true;
            }
        }
        args = System.Collections.Immutable.ImmutableArray<ITypeSymbol>.Empty;
        return false;
    }

    private static bool IsNullableValueType(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
}
