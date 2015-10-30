using System.Diagnostics;
using Marten.Codegen;
using Marten.Schema;
using Marten.Testing.Documents;

namespace Marten.Testing.Codegen
{
    public class Spike
    {
        public void tryit()
        {
            var mapping = new DocumentMapping(typeof(User));
            var writer = new SourceWriter();
            writer.StartNamespace("MyApplication");

            mapping.GenerateDocumentStorage(writer);

            writer.FinishBlock();

            Debug.WriteLine(writer.Code());
        }
    }
}