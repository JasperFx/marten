using System.Data.Common;
using Marten.Services;

namespace Marten.Linq
{
    public class DeserializeSelector<T> : ISelector<T>
    {
        private readonly ISerializer _serializer;

        public DeserializeSelector(ISerializer serializer)
        {
            _serializer = serializer;
        }

        public T Resolve(DbDataReader reader, IIdentityMap map)
        {
            return _serializer.FromJson<T>(reader.GetString(0));
        }

        public string[] SelectFields()
        {
            return new[] {"data"};
        }
    }
}