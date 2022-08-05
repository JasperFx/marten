using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Services.Json
{
    public class EventJsonTransformation
    {
        private readonly string _eventTypeName;
        private readonly Type _eventType;
        private readonly JsonTransformations _jsonTransformations;

        public EventJsonTransformation(string eventTypeName, Type eventType, JsonTransformations jsonTransformations)
        {
            _eventTypeName = eventTypeName;
            _eventType = eventType;
            _jsonTransformations = jsonTransformations;
        }

        public void Upcast(
            Func<ISerializer, Stream, object> transformStream,
            Func<ISerializer, DbDataReader, int, object> transformDbDataReader
        ) =>
            _jsonTransformations.Register(_eventType, transformStream, transformDbDataReader);

        public void Upcast(
            Func<ISerializer, Stream, CancellationToken, Task<object>> transformStream,
            Func<ISerializer, DbDataReader, int, CancellationToken, Task<object>> transformDbDataReader
        ) =>
            _jsonTransformations.Register(_eventType, transformStream, transformDbDataReader);
    }
}
