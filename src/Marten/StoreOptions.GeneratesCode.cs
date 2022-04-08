using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Reflection;
using Baseline;
using LamarCodeGeneration;
using Marten.Internal.CodeGeneration;
using Marten.Schema;
using Microsoft.Extensions.Hosting;

namespace Marten
{
    public partial class StoreOptions: ICodeFileCollection
    {
        private GenerationRules _rules;

        public IReadOnlyList<ICodeFile> BuildFiles()
        {
            Storage.BuildAllMappings();
            return Storage
                .AllDocumentMappings
                .Select(x => new DocumentProviderBuilder(x, this))
                .ToList();
        }

        GenerationRules ICodeFileCollection.Rules => CreateGenerationRules();

        string ICodeFileCollection.ChildNamespace { get; } = "DocumentStorage";

        /// <summary>
        /// The main application assembly. By default this is the entry assembly for the application,
        /// but you may need to change this in testing scenarios
        /// </summary>
        public Assembly ApplicationAssembly { get; set; }

        public bool SourceCodeWritingEnabled { get; set; } = true;

        // This would only be set for "additional" document stores
        internal string StoreName { get; set; } = "Marten";

        /// <summary>
        /// Root folder where generated code should be placed. By default, this is the IHostEnvironment.ContentRootPath
        /// </summary>
        public string GeneratedCodeOutputPath { get; set; }

        internal void ReadHostEnvironment(IHostEnvironment environment)
        {
            GeneratedCodeOutputPath ??= environment.ContentRootPath;
            if (environment.ApplicationName.IsNotEmpty())
            {
                ApplicationAssembly ??= Assembly.Load(environment.ApplicationName) ?? Assembly.GetEntryAssembly();
            }
        }

        internal GenerationRules CreateGenerationRules()
        {
            var rules = new GenerationRules(SchemaConstants.MartenGeneratedNamespace)
            {
                TypeLoadMode = GeneratedCodeMode,

                GeneratedCodeOutputPath = GeneratedCodeOutputPath ?? AppContext.BaseDirectory
                    .AppendPath("Internal", "Generated"),
                ApplicationAssembly = ApplicationAssembly ?? Assembly.GetEntryAssembly(),
                SourceCodeWritingEnabled = SourceCodeWritingEnabled
            };

            if (StoreName.IsNotEmpty() && StoreName != "Marten")
            {
                rules.GeneratedNamespace += "." + StoreName;
                rules.GeneratedCodeOutputPath = Path.Combine(rules.GeneratedCodeOutputPath, StoreName);
            }

            rules.ReferenceAssembly(GetType().Assembly);
            rules.ReferenceAssembly(Assembly.GetEntryAssembly());

            return rules;
        }
    }
}
