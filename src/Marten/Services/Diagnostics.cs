using Baseline;
using Marten.Linq;
using Marten.Schema;
using Npgsql;

namespace Marten.Services
{
    public class Diagnostics : IDiagnostics
    {
        private readonly IDocumentSchema _schema;

        public Diagnostics(IDocumentSchema schema)
        {
            _schema = schema;
        }

        /// <summary>
        /// Preview the dynamic code that Marten will generate to store and retrieve the 
        /// document type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
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