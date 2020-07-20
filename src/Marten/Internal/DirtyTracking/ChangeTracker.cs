using Marten.Internal.Operations;
using Newtonsoft.Json.Linq;

namespace Marten.Internal.DirtyTracking
{
    public class ChangeTracker<T>: IChangeTracker
    {
        private string _json;
        private readonly T _document;

        public ChangeTracker(IMartenSession session, T document)
        {
            _document = document;
            _json = session.Serializer.ToCleanJson(document);
        }

        public object Document => _document;

        public bool DetectChanges(IMartenSession session, out IStorageOperation operation)
        {
            var newJson = session.Serializer.ToCleanJson(_document);
            if (JToken.DeepEquals(JObject.Parse(_json), JObject.Parse(newJson)))
            {
                operation = null;
                return false;
            }

            operation = session
                .Tenant
                .Providers.StorageFor<T>()
                .DirtyTracking
                .Upsert(_document, session, session.Tenant);

            return true;
        }

        public void Reset(IMartenSession session)
        {
            _json = session.Serializer.ToCleanJson(_document);
        }
    }
}
