using System.Data.Common;
using Baseline;
using Marten.Schema;
using Marten.Services;

namespace Marten.Linq
{
    public class WholeDocumentSelector<T> : ISelector<T>
    {
        private readonly IResolver<T> _resolver;
        private readonly string[] _fields;

        public WholeDocumentSelector(IDocumentMapping mapping, IResolver<T> resolver)
        {
            _resolver = resolver;
            _fields = mapping.SelectFields();
        }

        public T Resolve(DbDataReader reader, IIdentityMap map)
        {
            return _resolver.Resolve(0, reader, map);
        }

        public string[] SelectFields()
        {
            return _fields;
        }

    }
}