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

namespace Marten.Events;

#nullable enable
public partial class EventGraph: ICodeFile
{
    private readonly Type _storageType;

    internal DocumentProvider<IEvent>? Provider { get; private set; }

    void ICodeFile.AssembleTypes(GeneratedAssembly assembly)
    {
        EventDocumentStorageGenerator.AssembleTypes(Options, assembly);
    }

    public bool AttachTypesSynchronously(GenerationRules rules, Assembly assembly, IServiceProvider? services,
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
