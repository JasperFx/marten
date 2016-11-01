using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;

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

        public string Resolve(DbDataReader reader, IIdentityMap map)
        {
            return reader.GetString(0);
        }

        public async Task<string> ResolveAsync(DbDataReader reader, IIdentityMap map, CancellationToken token)
        {
            return await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
        }

        public string[] SelectFields()
        {
            return new[] {_fieldName};
        }

        public string ToSelectClause(IQueryableDocument mapping)
        {
            return $"select {_fieldName} from {_document.Table.QualifiedName} as d";
        }
    }
}