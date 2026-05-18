using System;
using System.Reflection;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Events;
using Marten.Internal.Storage;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Events;

#nullable enable
public partial class EventGraph: ICodeFile
{
    internal DocumentProvider<IEvent>? Provider { get; private set; }

    void ICodeFile.AssembleTypes(GeneratedAssembly assembly)
    {
        // #4454 Phase 2: event storage is fully closed-shape — no Roslyn emit
        // for the event-store write path. The ICodeFile contract is preserved
        // for now so DocumentStore's ICodeFileCollection.BuildFiles aggregation
        // still walks EventGraph; Phase 5 retires the contract entirely.
    }

    public bool AttachTypesSynchronously(GenerationRules rules, Assembly assembly, IServiceProvider? services,
        string containingNamespace)
    {
        // Closed-shape adapter is constructed directly — no codegen lookup,
        // no JasperFx.RuntimeCompiler fallback. ProviderGraph triggers this
        // path on first IEvent storage request.
        var closedShape = new Marten.EventStorage.ClosedShapeEventDocumentStorage(Options);
        Provider = new DocumentProvider<IEvent>(null, closedShape, closedShape, closedShape, closedShape);
        return true;
    }

    Task<bool> ICodeFile.AttachTypes(GenerationRules rules, Assembly assembly, IServiceProvider? services,
        string containingNamespace)
    {
        var found = AttachTypesSynchronously(rules, assembly, services, containingNamespace);
        return Task.FromResult(found);
    }

    string ICodeFile.FileName => "EventStorage";

}
