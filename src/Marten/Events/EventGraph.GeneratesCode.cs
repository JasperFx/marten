using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Events;
using Marten.Events.CodeGeneration;
using Marten.Events.Projections;
using Marten.Internal.Storage;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Events;

#nullable enable
[UnconditionalSuppressMessage("Trimming", "IL2072",
    Justification = "Class-level: assigns the result of a reflective Type/MethodInfo lookup into a DAM-annotated target. Source types are preserved at the registration boundary.")]
public partial class EventGraph: ICodeFile
{
    private readonly Type _storageType;

    internal DocumentProvider<IEvent>? Provider { get; private set; }

    void ICodeFile.AssembleTypes(GeneratedAssembly assembly)
    {
        // #4410 / W4: skip codegen when the closed-shape adapter handles
        // the write path. Note: this only skips event-store codegen —
        // document storage and projection codegen are unaffected.
        if (UseClosedShapeStorage) return;

        EventDocumentStorageGenerator.AssembleTypes(Options, assembly);
    }

    public bool AttachTypesSynchronously(GenerationRules rules, Assembly assembly, IServiceProvider? services,
        string containingNamespace)
    {
        // #4410 / W4: when the closed-shape flag is on, skip the codegen
        // lookup entirely and construct the hand-written
        // ClosedShapeEventDocumentStorage adapter directly. The flag is
        // off by default in v9; default flips in v10 and the codegen
        // path is removed in v11.
        if (UseClosedShapeStorage)
        {
            var closedShape = new Marten.EventStorage.ClosedShapeEventDocumentStorage(Options);
            Provider = new DocumentProvider<IEvent>(null, closedShape, closedShape, closedShape, closedShape);
            return true;
        }

        var storageType = assembly.FindPreGeneratedType(containingNamespace,
            EventDocumentStorageGenerator.EventDocumentStorageTypeName);

        if (storageType == null)
        {
            Provider = null;
        }
        else
        {
            var storage = (EventDocumentStorage)Activator.CreateInstance(storageType, Options)!;
            Provider = new DocumentProvider<IEvent>(null, storage, storage, storage, storage);
        }

        return Provider != null;
    }

    Task<bool> ICodeFile.AttachTypes(GenerationRules rules, Assembly assembly, IServiceProvider? services,
        string containingNamespace)
    {
        var found = AttachTypesSynchronously(rules, assembly, services, containingNamespace);
        return Task.FromResult(found);
    }

    string ICodeFile.FileName => "EventStorage";

}
