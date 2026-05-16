using Microsoft.CodeAnalysis;

namespace Marten.SourceGenerator;

/// <summary>
/// Diagnostic descriptors emitted by the Marten source generator. IDs use the
/// <c>MTSG</c> prefix (Marten Source Generator) and are intentionally distinct
/// from any runtime Marten diagnostic ID.
/// </summary>
internal static class Diagnostics
{
    public static readonly DiagnosticDescriptor UnsupportedParameterType = new(
        id: "MTSG001",
        title: "Compiled query parameter type is not supported by the source generator",
        messageFormat: "Member '{0}' on compiled query '{1}' has type '{2}' which the Marten source generator does not yet recognize as a parameter, include reader, or statistics carrier. The member will be ignored — Marten's runtime planner will fall back to reflective binding if necessary.",
        category: "Marten.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Iteration 2 of the compiled-query source-gen PoC (#4405) covers BCL primitives, enums, arrays of supported element types, byte[], QueryStatistics, and Include collections. Custom value types and exotic Npgsql mappings will be added in subsequent iterations once the runtime registry is wired in.");
}
