using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LamarCodeGeneration;
using Marten.Internal.CodeGeneration;
using Marten.Internal.CompiledQueries;
using Marten.Schema;

namespace Marten
{
    public partial class StoreOptions: IGeneratesCode
    {
        public IReadOnlyList<ICodeFile> BuildFiles()
        {
            Storage.BuildAllMappings();
            var list = new List<ICodeFile>(
                Storage.AllDocumentMappings.Select(x => new DocumentPersistenceBuilder(x, this)));


            list.AddRange(_compiledQueryTypes.Select(x => new CompiledQueryCodeFile(x, this)));

            return list;
        }

        public string ChildNamespace { get; } = "DocumentStorage";

        internal GenerationRules CreateGenerationRules()
        {
            var rules = new GenerationRules(SchemaConstants.MartenGeneratedNamespace)
            {
                TypeLoadMode = GeneratedCodeMode
            };

            rules.ReferenceAssembly(GetType().Assembly);
            rules.ReferenceAssembly(Assembly.GetEntryAssembly());

            return rules;
        }
    }
}
