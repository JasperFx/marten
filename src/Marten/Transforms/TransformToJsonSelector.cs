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
    public class TransformToJsonSelector : ISelector<string>
    {
        private readonly IQueryableDocument _document;
        private readonly string _fieldName;

        public TransformToJsonSelector(string dataLocator, TransformFunction transform, IQueryableDocument document)
        {
            _document = document;
            _fieldName = $"{transform.Function.QualifiedName}({dataLocator}) as json";
        }

        public string Resolve(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            return reader.GetString(0);
        }

        public async Task<string> ResolveAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            return await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
        }

        public string[] SelectFields()
        {
            return new[] {_fieldName};
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