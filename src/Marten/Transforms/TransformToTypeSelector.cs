using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;

namespace Marten.Transforms
{
    public class TransformToTypeSelector<T> : ISelector<T>
    {
        private readonly IQueryableDocument _document;
        private readonly string _fieldName;

        public TransformToTypeSelector(string dataLocator, TransformFunction transform, IQueryableDocument document)
        {
            _document = document;
            _fieldName = $"{transform.Function.QualifiedName}({dataLocator}) as json";
        }

        public T Resolve(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            var json = reader.GetString(0);
            return map.Serializer.FromJson<T>(json);
        }

        public async Task<T> ResolveAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            var json = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
            return map.Serializer.FromJson<T>(json);
        }

        public string[] SelectFields()
        {
            return new[] { _fieldName };
        }

        public string ToSelectClause(IQueryableDocument mapping)
        {
            return $"select {_fieldName} from {_document.Table.QualifiedName} as d";
        }
    }
}