using System.Data.Common;
using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Services;

namespace Marten.Linq
{
    public class SelectTransformer<T> : ISelector<T>
    {
        private readonly TargetObject _target;
        private readonly IDocumentMapping _mapping;

        public SelectTransformer(IDocumentMapping mapping, TargetObject target)
        {
            _mapping = mapping;
            _target = target;
        }

        public T Resolve(DbDataReader reader, IIdentityMap map)
        {
            var json = reader.GetString(0);
            return map.Serializer.FromJson<T>(json);
        }

        public string[] SelectFields()
        {
            var jsonBuildObjectArgs = _target.Setters.Select(x => x.ToJsonBuildObjectPair(_mapping)).Join(", ");
            return new [] { $"json_build_object({jsonBuildObjectArgs}) as json"};
        }

    }
}