using Microsoft.CodeAnalysis;

namespace Marten.SourceGenerator;

/// <summary>
/// Static mapping from CLR types (expressed as Roslyn <see cref="ITypeSymbol"/>s)
/// to NpgsqlDbType enum names — the subset Marten's compiled-query codegen needs
/// today. Mirrors the table in
/// <c>Marten.Internal.CompiledQueries.ParameterUsage.npgsqlArrayDbTypeCodeFor</c>
/// plus the common simple-type cases handled by
/// <c>Weasel.Postgresql.PostgresqlProvider.determineParameterType</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is intentionally a hardcoded BCL-only table. The runtime
/// <c>PostgresqlProvider.Instance.ToParameterType(Type)</c> walks the live
/// Npgsql type-mapper plugin chain, which is not available at compile time. The
/// PoC's goal is to cover the high-frequency parameter types directly; less
/// common types fall through to <see cref="ResolveSimple"/> returning
/// <see langword="null"/>, which the generator treats as "unsupported — skip
/// this member with a diagnostic." We can extend this table as new compiled
/// query parameter shapes appear in real consumer assemblies.
/// </para>
/// </remarks>
internal static class NpgsqlTypeMap
{
    /// <summary>
    /// Returns the unqualified <c>NpgsqlDbType</c> enum member name for the given
    /// simple CLR type, or <see langword="null"/> if the type is not in the supported set.
    /// Nullable&lt;T&gt; is handled by the caller — pass the inner type.
    /// </summary>
    public static string? ResolveSimple(ITypeSymbol type)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_String: return "Varchar";
            case SpecialType.System_Int32: return "Integer";
            case SpecialType.System_Int64: return "Bigint";
            case SpecialType.System_Int16: return "Smallint";
            case SpecialType.System_UInt32: return "Oid";
            case SpecialType.System_Decimal: return "Numeric";
            case SpecialType.System_Single: return "Real";
            case SpecialType.System_Double: return "Double";
            case SpecialType.System_Boolean: return "Boolean";
            case SpecialType.System_Char: return "Char";
            case SpecialType.System_DateTime: return "Timestamp";
        }

        // Non-SpecialType BCL primitives are matched by full metadata name.
        var fullName = type.ToDisplayString();
        switch (fullName)
        {
            case "System.Guid": return "Uuid";
            case "System.DateTimeOffset": return "TimestampTz";
            case "System.TimeSpan": return "Interval";
            case "System.DateOnly": return "Date";
            case "System.TimeOnly": return "Time";
        }

        return null;
    }

    /// <summary>
    /// For an array CLR type returns the composite <c>NpgsqlDbType.Array | NpgsqlDbType.Element</c>
    /// expression, or <see langword="null"/> when the element type is unsupported. Caller is
    /// expected to have already verified the type is an array.
    /// </summary>
    public static string? ResolveArrayElement(ITypeSymbol elementType)
    {
        // byte[] is special — it maps to Bytea, not Array | <element>.
        if (elementType.SpecialType == SpecialType.System_Byte)
        {
            return null; // signal: caller should emit "Bytea" directly
        }

        return ResolveSimple(elementType);
    }
}
