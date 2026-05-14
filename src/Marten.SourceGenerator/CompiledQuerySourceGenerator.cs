using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Marten.SourceGenerator;

/// <summary>
/// PoC source generator (#4405) — finds <c>ICompiledQuery&lt;TDoc, TOut&gt;</c> implementations
/// in assemblies marked with <c>[JasperFxAssembly]</c> and emits typed scaffolding so
/// Marten's runtime never invokes <c>JasperFx.RuntimeCompiler</c> for compiled queries.
/// </summary>
/// <remarks>
/// <para>
/// <b>Iteration 2 — typed parameter binder.</b> For each discovered query type the
/// generator emits a <c>static class {QueryName}_CompiledQueryHandler</c> with:
/// </para>
/// <list type="bullet">
///   <item>The discovered type's metadata (full names of query/doc/output types).</item>
///   <item>Pre-classified member-name lists: <c>ParameterMemberNames</c>,
///         <c>IncludeMemberNames</c>, and <c>StatisticsMemberName</c>. The runtime
///         uses these to plan and bind without reflecting over the query type.</item>
///   <item>A <c>BindParameter(NpgsqlParameter, query, memberName, enumAsString)</c>
///         switch that does direct property/field reads — the AOT-safe replacement
///         for the codegen-emitted <c>parameters[N].Value = _query.MemberName</c>
///         lines in <c>ParameterUsage.GenerateCode</c>.</item>
/// </list>
/// <para>
/// Iteration 3 wires this scaffolding to the Marten runtime: the runtime registry
/// (keyed by query <see cref="System.Type"/>) is populated via a module-initializer
/// emitted alongside the binder, and <c>QueryCompiler.BuildQueryPlan</c> dispatches
/// through it instead of building a <c>CompiledQuerySourceBuilder.AssembleTypes</c>
/// assembly.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class CompiledQuerySourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Step 1 — gate on [JasperFxAssembly] being present on the consumer assembly.
        var hasJasperFxAssembly = context.CompilationProvider.Select((compilation, _) =>
            compilation.Assembly.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == "JasperFx.JasperFxAssemblyAttribute"));

        // Step 2 — find every class that implements ICompiledQuery<,> and pre-render
        // the scaffolding source. Pre-rendering inside the Select transform keeps the
        // incremental-cache key a simple string per discovered type.
        var compiledQueryTypes = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax c && c.BaseList is not null,
                transform: static (ctx, ct) => TryRenderHandler(ctx, ct))
            .Where(static x => x is not null)
            .Select(static (x, _) => x!.Value);

        // Step 3 — emit one handler class per discovered query type.
        var combined = compiledQueryTypes.Combine(hasJasperFxAssembly);
        context.RegisterSourceOutput(combined, static (spc, pair) =>
        {
            var (rendered, gated) = pair;
            if (!gated) return;

            foreach (var diag in rendered.Diagnostics)
            {
                spc.ReportDiagnostic(diag);
            }
            spc.AddSource(rendered.HintName, rendered.SourceText);
        });
    }

    private static RenderedHandler? TryRenderHandler(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.Node is not ClassDeclarationSyntax decl) return null;

        var symbol = ctx.SemanticModel.GetDeclaredSymbol(decl, ct) as INamedTypeSymbol;
        if (symbol is null || symbol.IsAbstract || symbol.IsGenericType) return null;

        // Find the ICompiledQuery<TDoc, TOut> interface implementation.
        var iface = symbol.AllInterfaces.FirstOrDefault(i =>
            i.IsGenericType
            && i.OriginalDefinition.ToDisplayString() == "Marten.Linq.ICompiledQuery<TDoc, TOut>");
        if (iface is null) return null;

        // Nested types would need outer-type qualification — out of PoC scope.
        if (symbol.ContainingType is not null) return null;

        return RenderHandler(symbol, iface);
    }

    private static RenderedHandler RenderHandler(INamedTypeSymbol query, INamedTypeSymbol compiledQueryIface)
    {
        // Default ToDisplayString() returns the C# keyword form for built-in types
        // (e.g., "bool", "int") which is invalid in `global::`-prefixed emit positions
        // (`global::bool` won't compile). FullyQualifiedFormat returns the canonical
        // `global::System.Boolean` form, which works uniformly for primitives and
        // user-defined types and is what we want at every code-emit position below.
        var fullName = Emit(query);
        var docType = Emit(compiledQueryIface.TypeArguments[0]);
        var outputType = Emit(compiledQueryIface.TypeArguments[1]);
        var simple = query.Name;
        var ns = query.ContainingNamespace.IsGlobalNamespace
            ? null
            : query.ContainingNamespace.ToDisplayString();

        var diagnostics = new List<Diagnostic>();
        var members = QueryMemberClassifier.Classify(query).ToArray();

        // Bucket members.
        var parameterNames = new List<string>();
        var includeNames = new List<string>();
        var includeReaderCtors = new StringBuilder();
        string? statisticsName = null;
        var caseBodies = new StringBuilder();

        foreach (var m in members)
        {
            switch (m.Kind)
            {
                case QueryMemberClassifier.MemberKind.Statistics:
                    statisticsName ??= m.Name;
                    break;
                case QueryMemberClassifier.MemberKind.Include:
                    includeNames.Add(m.Name);
                    AppendIncludeReaderCtor(includeReaderCtors, m, fullName);
                    break;
                case QueryMemberClassifier.MemberKind.SimpleParameter:
                case QueryMemberClassifier.MemberKind.EnumParameter:
                case QueryMemberClassifier.MemberKind.ArrayParameter:
                    parameterNames.Add(m.Name);
                    AppendCase(caseBodies, m);
                    break;
                case QueryMemberClassifier.MemberKind.Skip:
                    diagnostics.Add(Diagnostic.Create(
                        Diagnostics.UnsupportedParameterType,
                        location: query.Locations.FirstOrDefault() ?? Location.None,
                        m.Name, fullName, m.Type.ToDisplayString()));
                    break;
            }
        }

        var source = EmitSource(fullName, docType, outputType, simple, ns,
            parameterNames, includeNames, statisticsName,
            caseBodies.ToString(), includeReaderCtors.ToString());

        var hint = ns is null
            ? $"{simple}_CompiledQueryHandler.g.cs"
            : $"{ns.Replace('.', '_')}_{simple}_CompiledQueryHandler.g.cs";

        return new RenderedHandler(hint, source, diagnostics.ToArray());
    }

    private static void AppendCase(StringBuilder sb, QueryMemberClassifier.Classified m)
    {
        sb.Append("            case \"").Append(m.Name).Append("\":\n");

        switch (m.Kind)
        {
            case QueryMemberClassifier.MemberKind.SimpleParameter:
                if (m.Type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte })
                {
                    sb.Append("                parameter.NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Bytea;\n");
                    sb.Append("                parameter.Value = (object?)query.").Append(m.Name)
                      .Append(" ?? global::System.DBNull.Value;\n");
                }
                else
                {
                    var pg = NpgsqlTypeMap.ResolveSimple(m.Type)!;
                    sb.Append("                parameter.NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.")
                      .Append(pg).Append(";\n");
                    // Value types (and string) — emit a straight assignment; runtime caller
                    // is responsible for not passing default values through to the planner.
                    sb.Append("                parameter.Value = (object?)query.").Append(m.Name)
                      .Append(" ?? global::System.DBNull.Value;\n");
                }
                break;

            case QueryMemberClassifier.MemberKind.EnumParameter:
                // The runtime planner reads StoreOptions.Serializer().EnumStorage at codegen
                // time today. The generator can't see that — the consumer's StoreOptions is
                // configured at runtime. So we emit BOTH branches and dispatch on the
                // `enumAsString` parameter passed by the caller. The runtime registry passes
                // the right value once when populating the binder cache for this query type.
                sb.Append("                if (enumAsString)\n");
                sb.Append("                {\n");
                sb.Append("                    parameter.NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Varchar;\n");
                sb.Append("                    parameter.Value = query.").Append(m.Name).Append(".ToString();\n");
                sb.Append("                }\n");
                sb.Append("                else\n");
                sb.Append("                {\n");
                sb.Append("                    parameter.NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Integer;\n");
                sb.Append("                    parameter.Value = (int)query.").Append(m.Name).Append(";\n");
                sb.Append("                }\n");
                break;

            case QueryMemberClassifier.MemberKind.ArrayParameter:
                var elementPg = NpgsqlTypeMap.ResolveArrayElement(m.ElementType!)!;
                sb.Append("                parameter.NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Array | global::NpgsqlTypes.NpgsqlDbType.")
                  .Append(elementPg).Append(";\n");
                sb.Append("                parameter.Value = (object?)query.").Append(m.Name)
                  .Append(" ?? global::System.DBNull.Value;\n");
                break;
        }

        sb.Append("                break;\n");
    }

    /// <summary>
    /// Emits one <c>Include.ReaderTo*</c> factory invocation for a single Include
    /// member, in the order the runtime registry's
    /// <c>AttachIncludeReaders</c> array expects. The shape (Action / IList / IDictionary)
    /// determines which factory + how many type arguments we close.
    /// </summary>
    private static void AppendIncludeReaderCtor(
        StringBuilder sb, QueryMemberClassifier.Classified m, string queryFullName)
    {
        // Emit() returns the global::-prefixed canonical form, so no manual prefix needed here.
        switch (m.IncludeShape)
        {
            case QueryMemberClassifier.IncludeShape.Action:
                sb.Append("                global::Marten.Linq.Includes.Include.ReaderToAction<")
                  .Append(Emit(m.ElementType!)).Append(">(session, query.")
                  .Append(m.Name).Append("),\n");
                break;
            case QueryMemberClassifier.IncludeShape.List:
                sb.Append("                global::Marten.Linq.Includes.Include.ReaderToList<")
                  .Append(Emit(m.ElementType!)).Append(">(session, query.")
                  .Append(m.Name).Append("),\n");
                break;
            case QueryMemberClassifier.IncludeShape.Dictionary:
                // Include.ReaderToDictionary<T, TId> — element type is the value, key type is TId.
                sb.Append("                global::Marten.Linq.Includes.Include.ReaderToDictionary<")
                  .Append(Emit(m.ElementType!)).Append(", ")
                  .Append(Emit(m.DictionaryKeyType!)).Append(">(session, query.")
                  .Append(m.Name).Append("),\n");
                break;
            default:
                throw new InvalidOperationException(
                    $"Include member {queryFullName}.{m.Name} has IncludeShape={m.IncludeShape}; expected Action/List/Dictionary.");
        }
    }

    private static string EmitSource(
        string fullName, string docType, string outputType, string simple, string? ns,
        List<string> parameterNames, List<string> includeNames, string? statisticsName,
        string caseBodies, string includeReaderCtors)
    {
        var nsBlock = ns is null ? string.Empty : $"namespace {ns};\n\n";
        var parametersLiteral = RenderStringArray(parameterNames);
        var includesLiteral = RenderStringArray(includeNames);
        var statsLiteral = statisticsName is null ? "null" : $"\"{statisticsName}\"";

        // AttachIncludeReaders body — empty array literal when the query has no
        // includes, otherwise an array initializer with one IIncludeReader per
        // include member in declaration order.
        string attachIncludeReadersBody;
        if (includeNames.Count == 0)
        {
            attachIncludeReadersBody = "        return global::System.Array.Empty<global::Marten.Linq.Includes.IIncludeReader>();";
        }
        else
        {
            attachIncludeReadersBody =
                "        return new global::Marten.Linq.Includes.IIncludeReader[]\n" +
                "        {\n" +
                includeReaderCtors.TrimEnd('\n').TrimEnd(',') + "\n" +
                "        };";
        }

        // ReadStatistics body — returns the query's QueryStatistics member (or a
        // fresh one if null), or null if the query type has no statistics member.
        var readStatisticsBody = statisticsName is null
            ? "        return null;"
            : $"        return query.{statisticsName} ?? new global::Marten.Linq.QueryStatistics();";

        // The module-init descriptor ctor always supplies both new delegates;
        // pass-through static lambdas (zero allocation) box the typed methods.
        // (`fullName` is already global::-prefixed via Emit() so no manual prefix here.)
        var attachIncludeReadersArg = $"static (session, query) => {simple}_CompiledQueryHandler.AttachIncludeReaders(session, ({fullName})query)";
        var readStatisticsArg = $"static query => {simple}_CompiledQueryHandler.ReadStatistics(({fullName})query)";

        return $$"""
            // <auto-generated/>
            // Marten.SourceGenerator — #4405 PoC iteration 3.
            // Discovered compiled query: {{fullName}}
            //   Doc type:    {{docType}}
            //   Output type: {{outputType}}
            //
            // This file emits two things for the discovered compiled query type:
            //  1. {{simple}}_CompiledQueryHandler — a static class holding the
            //     direct-property-read parameter binder + member-classification
            //     arrays. This is the AOT-safe replacement for the per-query type
            //     JasperFx.RuntimeCompiler emits today.
            //  2. {{simple}}_CompiledQueryHandler_Registration — a [ModuleInitializer]
            //     shim that registers the handler with the runtime registry at
            //     assembly load. The consumer never calls Register directly;
            //     opt-in is implicit via the package reference + [JasperFxAssembly].

            #nullable enable

            {{nsBlock}}internal static class {{simple}}_CompiledQueryHandler
            {
                public const string DiscoveredQueryFullName = "{{fullName}}";
                public const string DocTypeFullName = "{{docType}}";
                public const string OutputTypeFullName = "{{outputType}}";

                /// <summary>Member names treated as query parameters (excludes includes + statistics).</summary>
                public static readonly string[] ParameterMemberNames = {{parametersLiteral}};

                /// <summary>Member names that hold Include readers (Action&lt;&gt;, IList&lt;&gt;, IDictionary&lt;,&gt;).</summary>
                public static readonly string[] IncludeMemberNames = {{includesLiteral}};

                /// <summary>Member name carrying QueryStatistics, or null if none.</summary>
                public static readonly string? StatisticsMemberName = {{statsLiteral}};

                /// <summary>
                /// Direct-property-read parameter binder for compiled query {{simple}}.
                /// Replaces the per-property reflection in JasperFx.RuntimeCompiler with
                /// a straight property access. See Marten issue #4405.
                /// </summary>
                /// <param name="parameter">The NpgsqlParameter to populate (DbType + Value).</param>
                /// <param name="query">The compiled query instance.</param>
                /// <param name="memberName">Name of the query member to bind from.</param>
                /// <param name="enumAsString">
                /// True when the document store's serializer uses <c>EnumStorage.AsString</c>;
                /// false for <c>EnumStorage.AsInteger</c>. Ignored for non-enum members.
                /// </param>
                public static void BindParameter(
                    global::Npgsql.NpgsqlParameter parameter,
                    {{fullName}} query,
                    string memberName,
                    bool enumAsString)
                {
                    switch (memberName)
                    {
            {{caseBodies.TrimEnd('\n')}}
                        default:
                            throw new global::System.ArgumentOutOfRangeException(
                                nameof(memberName), memberName,
                                "Member is not a recognized compiled query parameter on {{fullName}}.");
                    }
                }

                /// <summary>
                /// Builds the array of <c>IIncludeReader</c> instances for this query's
                /// Include members, one per member in declaration order. Each reader is
                /// constructed via the appropriate <c>Include.ReaderTo*</c> factory closed
                /// over the consumer-declared element type — no <c>MakeGenericMethod</c>
                /// at runtime. Returns an empty array when the query has no Include members.
                /// </summary>
                public static global::Marten.Linq.Includes.IIncludeReader[] AttachIncludeReaders(
                    global::Marten.Internal.IMartenSession session,
                    {{fullName}} query)
                {
            {{attachIncludeReadersBody}}
                }

                /// <summary>
                /// Reads the <c>QueryStatistics</c> member from the query instance,
                /// substituting a fresh empty <c>QueryStatistics</c> if the property
                /// holds <see langword="null"/>. Returns <see langword="null"/> for query
                /// types that have no statistics member.
                /// </summary>
                public static global::Marten.Linq.QueryStatistics? ReadStatistics({{fullName}} query)
                {
            {{readStatisticsBody}}
                }
            }

            /// <summary>
            /// Module-initializer shim that registers {{simple}}_CompiledQueryHandler
            /// with the runtime <c>Marten.Internal.CompiledQueries.CompiledQueryHandlerRegistry</c>
            /// at assembly load. The boxing adapter is a <c>static</c> lambda — captures
            /// nothing — so the per-call hot path is zero-allocation.
            /// </summary>
            internal static class {{simple}}_CompiledQueryHandler_Registration
            {
                [global::System.Runtime.CompilerServices.ModuleInitializer]
                internal static void Register()
                {
                    global::Marten.Internal.CompiledQueries.CompiledQueryHandlerRegistry.Register(
                        typeof({{fullName}}),
                        new global::Marten.Internal.CompiledQueries.CompiledQueryHandlerDescriptor(
                            queryType: typeof({{fullName}}),
                            docType: typeof({{docType}}),
                            outputType: typeof({{outputType}}),
                            parameterMemberNames: {{simple}}_CompiledQueryHandler.ParameterMemberNames,
                            includeMemberNames: {{simple}}_CompiledQueryHandler.IncludeMemberNames,
                            statisticsMemberName: {{simple}}_CompiledQueryHandler.StatisticsMemberName,
                            bindParameter: static (parameter, query, memberName, enumAsString)
                                => {{simple}}_CompiledQueryHandler.BindParameter(
                                    parameter, ({{fullName}})query, memberName, enumAsString),
                            attachIncludeReaders: {{attachIncludeReadersArg}},
                            readStatistics: {{readStatisticsArg}}));
                }
            }
            """;
    }

    /// <summary>
    /// Produces a fully-qualified, <c>global::</c>-rooted type reference suitable
    /// for emit positions. Uses Roslyn's <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/>
    /// so primitives come out as <c>global::System.Boolean</c> rather than the
    /// C# keyword <c>bool</c> (which is illegal after <c>global::</c>).
    /// </summary>
    private static string Emit(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string RenderStringArray(List<string> values)
    {
        if (values.Count == 0) return "global::System.Array.Empty<string>()";
        var joined = string.Join(", ", values.Select(v => $"\"{v}\""));
        return $"new string[] {{ {joined} }}";
    }

    private readonly record struct RenderedHandler(string HintName, string SourceText, Diagnostic[] Diagnostics);
}
