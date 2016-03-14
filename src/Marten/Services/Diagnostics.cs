using Baseline;
using Marten.Schema;

namespace Marten.Services
{
    public class Diagnostics : IDiagnostics
    {
        private readonly IDocumentSchema _schema;

        public Diagnostics(IDocumentSchema schema)
        {
            _schema = schema;
        }

        public string DocumentStorageCodeFor<T>()
        {
            var documentMapping = _schema.MappingFor(typeof (T));
            if (documentMapping is DocumentMapping)
            {
                return DocumentStorageBuilder.GenerateDocumentStorageCode(new[] {documentMapping.As<DocumentMapping>()});
            }

            return $"Document Storage for {typeof (T).FullName} is not generated";
        }
    }
}