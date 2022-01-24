using System.Collections.Generic;
using System.Linq;
using LamarCodeGeneration;
using Marten.Internal.CodeGeneration;
using Marten.Internal.CompiledQueries;

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
    }
}
