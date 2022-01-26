using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
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

        string IGeneratesCode.ChildNamespace { get; } = "DocumentStorage";

        /// <summary>
        /// The main application assembly. By default this is the entry assembly for the application,
        /// but you may need to change this in testing scenarios
        /// </summary>
        public Assembly ApplicationAssembly { get; set; } = Assembly.GetEntryAssembly();


        internal GenerationRules CreateGenerationRules()
        {
            var rules = new GenerationRules(SchemaConstants.MartenGeneratedNamespace)
            {
                TypeLoadMode = GeneratedCodeMode,
                GeneratedCodeOutputPath = AppContext.BaseDirectory.ParentDirectory().ParentDirectory().ParentDirectory()
                    .AppendPath("Internal", "Generated"),
                ApplicationAssembly = ApplicationAssembly
            };

            rules.ReferenceAssembly(GetType().Assembly);
            rules.ReferenceAssembly(Assembly.GetEntryAssembly());

            return rules;
        }
    }
}
