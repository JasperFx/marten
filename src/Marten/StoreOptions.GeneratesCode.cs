using System;
using System.Reflection;
using System.Threading.Tasks;
using LamarCodeGeneration;
using LamarCodeGeneration.Model;

namespace Marten
{
    public partial class StoreOptions : IGeneratesCode
    {
        public IServiceVariableSource AssemblyTypes(GenerationRules rules, GeneratedAssembly assembly)
        {
            return null;
        }

        public Task AttachPreBuiltTypes(GenerationRules rules, Assembly assembly, IServiceProvider services)
        {
            return Task.CompletedTask;
        }

        public Task AttachGeneratedTypes(GenerationRules rules, IServiceProvider services)
        {
            return Task.CompletedTask;
        }

        public string CodeType => "DocumentStorage";
    }
}
