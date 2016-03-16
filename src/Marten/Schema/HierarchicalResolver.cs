using System.Data.Common;
using Marten.Services;

namespace Marten.Schema
{
    public class HierarchicalResolver<T> : Resolver<T> where T : class
    {
        private readonly DocumentMapping _hierarchy;

        public HierarchicalResolver(DocumentMapping hierarchy)
        {
            _hierarchy = hierarchy;
        }

        public override T Resolve(int startingIndex, DbDataReader reader, IIdentityMap map)
        {
            var json = reader.GetString(startingIndex);
            var id = reader[startingIndex + 1];
            var typeAlias = reader.GetString(startingIndex + 2);

            return map.Get<T>(id, _hierarchy.TypeFor(typeAlias), json);
        }
    }
}