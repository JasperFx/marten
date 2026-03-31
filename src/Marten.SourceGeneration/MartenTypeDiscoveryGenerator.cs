using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Marten.SourceGeneration;

/// <summary>
///     Incremental source generator that discovers Marten document types, projection types,
///     and event types at compile time, emitting a static manifest class that can be used
///     to bypass runtime assembly scanning on startup.
/// </summary>
[Generator]
public class MartenTypeDiscoveryGenerator : IIncrementalGenerator
{
    // Attribute-based document discovery
    private const string DocumentAliasAttributeFullName = "Marten.Schema.DocumentAliasAttribute";

    // Projection base class names (unbound generic or non-generic)
    private static readonly string[] ProjectionBaseTypeNames = new[]
    {
        "Marten.Events.Aggregation.SingleStreamProjection",
        "Marten.Events.Projections.MultiStreamProjection",
        "Marten.Events.Projections.EventProjection"
    };

    // Method names used in projections that take event parameters
    private static readonly HashSet<string> ApplyMethodNames = new HashSet<string>
    {
        "Apply", "Create", "ShouldDelete"
    };

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Pipeline 1: Find classes with [DocumentAlias] attribute (document types)
        var documentTypesByAttribute = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsClassWithAttributes(node),
                transform: static (ctx, _) => GetDocumentAliasType(ctx))
            .Where(static info => info != null);

        // Pipeline 2: Find classes used with IDocumentSession.Store<T>() or IQuerySession.Query<T>()
        var documentTypesByUsage = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsGenericInvocation(node),
                transform: static (ctx, _) => GetSessionUsageType(ctx))
            .Where(static info => info != null);

        // Pipeline 3: Find projection types (classes inheriting from projection base classes)
        var projectionTypes = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsClassWithBaseList(node),
                transform: static (ctx, _) => GetProjectionTypeInfo(ctx))
            .Where(static info => info != null);

        // Combine all pipelines with the compilation
        var allData = context.CompilationProvider
            .Combine(documentTypesByAttribute.Collect())
            .Combine(documentTypesByUsage.Collect())
            .Combine(projectionTypes.Collect());

        context.RegisterSourceOutput(allData, static (spc, source) =>
        {
            var compilation = source.Left.Left.Left;
            var docsByAttribute = source.Left.Left.Right;
            var docsByUsage = source.Left.Right;
            var projections = source.Right;

            Execute(compilation, docsByAttribute, docsByUsage, projections, spc);
        });
    }

    private static bool IsClassWithAttributes(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax classDecl
               && classDecl.AttributeLists.Count > 0;
    }

    private static bool IsClassWithBaseList(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax classDecl
               && classDecl.BaseList != null
               && classDecl.BaseList.Types.Count > 0;
    }

    private static bool IsGenericInvocation(SyntaxNode node)
    {
        // Looking for method calls like session.Store<T>() or session.Query<T>()
        return node is InvocationExpressionSyntax invocation
               && invocation.Expression is MemberAccessExpressionSyntax memberAccess
               && memberAccess.Name is GenericNameSyntax;
    }

    private static DiscoveredTypeInfo GetDocumentAliasType(GeneratorSyntaxContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var model = context.SemanticModel;
        var classSymbol = model.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;

        if (classSymbol == null) return null;

        foreach (var attr in classSymbol.GetAttributes())
        {
            var attrName = attr.AttributeClass?.ToDisplayString();
            if (attrName == DocumentAliasAttributeFullName)
            {
                return new DiscoveredTypeInfo(
                    classSymbol.ToDisplayString(),
                    DiscoveredKind.Document);
            }
        }

        return null;
    }

    private static DiscoveredTypeInfo GetSessionUsageType(GeneratorSyntaxContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;
        var genericName = (GenericNameSyntax)memberAccess.Name;

        var methodName = genericName.Identifier.Text;
        if (methodName != "Store" && methodName != "Query") return null;

        var model = context.SemanticModel;
        var symbolInfo = model.GetSymbolInfo(invocation);
        var methodSymbol = symbolInfo.Symbol as IMethodSymbol ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

        if (methodSymbol == null) return null;

        // Check that the containing type is IDocumentSession or IQuerySession
        var containingType = methodSymbol.ContainingType?.ToDisplayString();
        if (containingType == null) return null;

        bool isDocumentSession = containingType.StartsWith("Marten.IDocumentSession")
                                 || containingType.StartsWith("Marten.IQuerySession");
        if (!isDocumentSession) return null;

        // Get the type argument
        if (methodSymbol.TypeArguments.Length == 0) return null;
        var typeArg = methodSymbol.TypeArguments[0];

        // Skip open generic types
        if (typeArg is ITypeParameterSymbol) return null;

        return new DiscoveredTypeInfo(
            typeArg.ToDisplayString(),
            DiscoveredKind.Document);
    }

    private static ProjectionTypeInfo GetProjectionTypeInfo(GeneratorSyntaxContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var model = context.SemanticModel;
        var classSymbol = model.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;

        if (classSymbol == null || classSymbol.IsAbstract) return null;

        // Walk the base type chain to find projection base classes
        var currentBase = classSymbol.BaseType;
        while (currentBase != null)
        {
            var baseFullName = currentBase.OriginalDefinition.ToDisplayString();

            foreach (var projectionBase in ProjectionBaseTypeNames)
            {
                if (baseFullName.StartsWith(projectionBase))
                {
                    // Discovered a projection type. Now find event types from Apply/Create methods.
                    var eventTypes = DiscoverEventTypes(classSymbol);
                    return new ProjectionTypeInfo(
                        classSymbol.ToDisplayString(),
                        baseFullName,
                        eventTypes);
                }
            }

            currentBase = currentBase.BaseType;
        }

        return null;
    }

    private static List<string> DiscoverEventTypes(INamedTypeSymbol projectionClass)
    {
        var eventTypes = new List<string>();
        var seen = new HashSet<string>();

        // Walk up the type hierarchy to find Apply/Create methods
        var current = projectionClass;
        while (current != null)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IMethodSymbol method && ApplyMethodNames.Contains(method.Name))
                {
                    // The first parameter (or second if first is the aggregate) is typically the event type
                    foreach (var param in method.Parameters)
                    {
                        var paramType = param.Type;
                        if (paramType is ITypeParameterSymbol) continue;

                        var paramTypeName = paramType.ToDisplayString();

                        // Skip known Marten/infrastructure types
                        if (paramTypeName.StartsWith("Marten.") ||
                            paramTypeName.StartsWith("JasperFx.") ||
                            paramTypeName.StartsWith("System.") ||
                            paramTypeName == "object")
                            continue;

                        if (seen.Add(paramTypeName))
                        {
                            eventTypes.Add(paramTypeName);
                        }
                    }
                }
            }

            current = current.BaseType;
        }

        return eventTypes;
    }

    private static void Execute(
        Compilation compilation,
        ImmutableArray<DiscoveredTypeInfo> docsByAttribute,
        ImmutableArray<DiscoveredTypeInfo> docsByUsage,
        ImmutableArray<ProjectionTypeInfo> projections,
        SourceProductionContext context)
    {
        // Collect all unique document types
        var documentTypes = new HashSet<string>();
        foreach (var doc in docsByAttribute)
        {
            if (doc != null) documentTypes.Add(doc.FullTypeName);
        }
        foreach (var doc in docsByUsage)
        {
            if (doc != null) documentTypes.Add(doc.FullTypeName);
        }

        // Collect all unique projection types and event types
        var projectionTypeNames = new HashSet<string>();
        var eventTypeNames = new HashSet<string>();

        foreach (var proj in projections)
        {
            if (proj == null) continue;
            projectionTypeNames.Add(proj.FullTypeName);
            foreach (var eventType in proj.EventTypes)
            {
                eventTypeNames.Add(eventType);
            }
        }

        // Only emit if we found something
        if (documentTypes.Count == 0 && projectionTypeNames.Count == 0 && eventTypeNames.Count == 0)
            return;

        var source = DiscoveredMartenTypesEmitter.Emit(
            documentTypes.OrderBy(x => x).ToList(),
            projectionTypeNames.OrderBy(x => x).ToList(),
            eventTypeNames.OrderBy(x => x).ToList());

        context.AddSource("DiscoveredMartenTypes.g.cs",
            SourceText.From(source, Encoding.UTF8));
    }
}

internal sealed class DiscoveredTypeInfo
{
    public DiscoveredTypeInfo(string fullTypeName, DiscoveredKind kind)
    {
        FullTypeName = fullTypeName;
        Kind = kind;
    }

    public string FullTypeName { get; }
    public DiscoveredKind Kind { get; }
}

internal enum DiscoveredKind
{
    Document,
    Projection,
    Event
}

internal sealed class ProjectionTypeInfo
{
    public ProjectionTypeInfo(string fullTypeName, string baseTypeName, List<string> eventTypes)
    {
        FullTypeName = fullTypeName;
        BaseTypeName = baseTypeName;
        EventTypes = eventTypes;
    }

    public string FullTypeName { get; }
    public string BaseTypeName { get; }
    public List<string> EventTypes { get; }
}
