using System.Data.Common;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;
using Marten.Util;

namespace Marten.Transforms
{
    public class TransformToTypeSelector<T> : ISelector<T>
    {
        private readonly IQueryableDocument _document;
        private readonly string _fieldName;

        public TransformToTypeSelector(string dataLocator, TransformFunction transform, IQueryableDocument document)
        {
            _document = document;
            _fieldName = $"{transform.Identifier}({dataLocator}) as json";
        }

        public T Resolve(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            var json = reader.GetTextReader(0);
            return map.Serializer.FromJson<T>(json);
        }

        public Task<T> ResolveAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            var json = reader.GetTextReader(0);
            //var json = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
            var document = map.Serializer.FromJson<T>(json);
            return Task.FromResult(document);
        }

        public string[] SelectFields()
        {
            return new[] { _fieldName };
        }

        public void WriteSelectClause(CommandBuilder sql, IQueryableDocument mapping)
        {
            sql.Append("select ");
            sql.Append(_fieldName);
            sql.Append(" from ");
            sql.Append(_document.Table.QualifiedName);
            sql.Append(" as d");
        }
    }
}