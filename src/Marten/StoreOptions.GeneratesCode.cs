using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
using LamarCodeGeneration;
using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten
{
    public partial class StoreOptions: ICodeFileCollection
    {
        public IReadOnlyList<ICodeFile> BuildFiles()
        {
            Storage.BuildAllMappings();
            return Storage
                .AllDocumentMappings
                .Select(x => new DocumentProviderBuilder(x, this))
                .ToList();
        }

        string ICodeFileCollection.ChildNamespace { get; } = "DocumentStorage";

        /// <summary>
        /// The main application assembly. By default this is the entry assembly for the application,
        /// but you may need to change this in testing scenarios
        /// </summary>
        public Assembly ApplicationAssembly { get; set; } = Assembly.GetEntryAssembly();

        public bool SourceCodeWritingEnabled { get; set; } = true;


        internal GenerationRules CreateGenerationRules()
        {
            var rules = new GenerationRules(SchemaConstants.MartenGeneratedNamespace)
            {
                TypeLoadMode = GeneratedCodeMode,

                GeneratedCodeOutputPath = AppContext.BaseDirectory.ParentDirectory().ParentDirectory().ParentDirectory()
                    .AppendPath("Internal", "Generated"),
                ApplicationAssembly = ApplicationAssembly,
                SourceCodeWritingEnabled = SourceCodeWritingEnabled
            };

            rules.ReferenceAssembly(GetType().Assembly);
            rules.ReferenceAssembly(Assembly.GetEntryAssembly());

            return rules;
        }
    }
}
