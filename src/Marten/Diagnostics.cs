using System;
using System.Data;
using System.Linq;
using Marten.Linq;
using Marten.Schema;

namespace Marten
{
    public class Diagnostics : IDiagnostics
    {
        private readonly IDocumentSchema _schema;
        private readonly IMartenQueryExecutor _executor;

        public Diagnostics(IDocumentSchema schema, IMartenQueryExecutor executor)
        {
            _schema = schema;
            _executor = executor;
        }

        public IDbCommand CommandFor<T>(IQueryable<T> queryable)
        {
            if (queryable is MartenQueryable<T>)
            {
                return _executor.BuildCommand<T>(queryable);
            }

            throw new ArgumentOutOfRangeException(nameof(queryable), "This mechanism can only be used for MartenQueryable<T> objects");
        }

        public string DocumentStorageCodeFor<T>()
        {
            return DocumentStorageBuilder.GenerateDocumentStorageCode(new[] { _schema.MappingFor(typeof(T)) });
        }
    }
}