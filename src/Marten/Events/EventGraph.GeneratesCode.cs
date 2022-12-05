using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using Marten.Events.CodeGeneration;
using Marten.Events.Projections;
using Marten.Internal.Storage;
using Marten.Util;

namespace Marten.Events;

public partial class EventGraph: ICodeFileCollection, ICodeFile
{
    private Type _storageType;

    internal DocumentProvider<IEvent> Provider { get; private set; }

    void ICodeFile.AssembleTypes(GeneratedAssembly assembly)
    {
        EventDocumentStorageGenerator.AssembleTypes(Options, assembly);
    }

    public bool AttachTypesSynchronously(GenerationRules rules, Assembly assembly, IServiceProvider services,
        string containingNamespace)
    {
        var storageType = assembly.FindPreGeneratedType(containingNamespace,
            EventDocumentStorageGenerator.EventDocumentStorageTypeName);

        if (storageType == null)
        {
            Provider = null;
        }
        else
        {
            var storage = (EventDocumentStorage)Activator.CreateInstance(storageType, Options);
            Provider = new DocumentProvider<IEvent>(null, storage, storage, storage, storage);
        }

        return Provider != null;
    }

    Task<bool> ICodeFile.AttachTypes(GenerationRules rules, Assembly assembly, IServiceProvider services,
        string containingNamespace)
    {
        var found = AttachTypesSynchronously(rules, assembly, services, containingNamespace);
        return Task.FromResult(found);
    }

    string ICodeFile.FileName => "EventStorage";

    public GenerationRules Rules => Options.CreateGenerationRules();

    IReadOnlyList<ICodeFile> ICodeFileCollection.BuildFiles()
    {
        var list = new List<ICodeFile> { this };

        var projections = Options.Projections.All.OfType<ICodeFile>();
        list.AddRange(projections);

        foreach (var projection in projections.OfType<GeneratedProjection>()) projection.StoreOptions = Options;

        return list;
    }

    string ICodeFileCollection.ChildNamespace { get; } = "EventStore";
}
