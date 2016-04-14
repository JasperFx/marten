using System.Data.Common;
using Marten.Schema;
using Marten.Services;

namespace Marten.Linq
{
    public class SelectTransformer<T> : BasicSelector, ISelector<T>
    {
        public SelectTransformer(IDocumentMapping mapping, TargetObject target) 
            : base(target.ToSelectField(mapping))
        {
        }

        public T Resolve(DbDataReader reader, IIdentityMap map)
        {
            var json = reader.GetString(0);
            return map.Serializer.FromJson<T>(json);
        }
    }
}