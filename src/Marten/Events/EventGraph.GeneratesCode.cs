using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using LamarCodeGeneration;
using LamarCodeGeneration.Model;
using Marten.Events.CodeGeneration;
using Marten.Events.Projections;
using Marten.Schema;

namespace Marten.Events
{
    public partial class EventGraph : IGeneratesCode
    {
        IServiceVariableSource IGeneratesCode.AssemblyTypes(GenerationRules rules, GeneratedAssembly assembly)
        {
            rules.ApplicationNamespace = SchemaConstants.MartenGeneratedNamespace;
            EventDocumentStorageGenerator.AssembleTypes(Options, assembly);

            var projections = Options.Projections.All.OfType<IGeneratedProjection>();
            foreach (var projection in projections)
            {
                projection.AssembleTypes(assembly, Options);
            }

            return null;
        }

        Task IGeneratesCode.AttachPreBuiltTypes(GenerationRules rules, Assembly assembly, IServiceProvider services)
        {
            var provider = EventDocumentStorageGenerator.BuildProviderFromAssembly(assembly, Options);
            if (provider != null)
            {
                Options.Providers.Append(provider);
            }

            var projections = Options.Projections.All.OfType<IGeneratedProjection>();
            foreach (var projection in projections)
            {
                projection.AttachTypes(assembly, Options);
            }

            return Task.CompletedTask;
        }

        Task IGeneratesCode.AttachGeneratedTypes(GenerationRules rules, IServiceProvider services)
        {
            throw new NotSupportedException();
        }

        string IGeneratesCode.CodeType => "Events";
    }
}
