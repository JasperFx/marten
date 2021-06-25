using System;
using System.Reflection;
using System.Threading.Tasks;
using LamarCodeGeneration;
using LamarCodeGeneration.Model;
using Marten.Events.CodeGeneration;
using Marten.Schema;

namespace Marten.Events
{
    public partial class EventGraph : IGeneratesCode
    {
        IServiceVariableSource IGeneratesCode.AssemblyTypes(GenerationRules rules, GeneratedAssembly assembly)
        {
            rules.ApplicationNamespace = SchemaConstants.MartenGeneratedNamespace;
            EventDocumentStorageGenerator.AssembleTypes(Options, assembly);

            // TODO -- projections

            return null;
        }

        Task IGeneratesCode.AttachPreBuiltTypes(GenerationRules rules, Assembly assembly, IServiceProvider services)
        {
            var provider = EventDocumentStorageGenerator.BuildProviderFromAssembly(assembly, Options);
            Options.Providers.Append(provider);

            // TODO -- projections

            return Task.CompletedTask;
        }

        Task IGeneratesCode.AttachGeneratedTypes(GenerationRules rules, IServiceProvider services)
        {
            throw new NotSupportedException();
        }

        string IGeneratesCode.CodeType => "Events";
    }
}
