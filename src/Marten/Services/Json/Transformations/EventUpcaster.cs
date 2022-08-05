using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Services.Json
{
    public class EventUpcaster
    {
        private readonly string _eventTypeName;
        private readonly JsonTransformations _jsonTransformations;

        public EventUpcaster(string eventTypeName, JsonTransformations jsonTransformations)
        {
            _eventTypeName = eventTypeName;
            _jsonTransformations = jsonTransformations;
        }

        public void With(
            Type type,
            Func<ISerializer, Stream, object> transformStream,
            Func<ISerializer, DbDataReader, int, object> transformDbDataReader
        ) =>
            _jsonTransformations.Register(type, transformStream, transformDbDataReader);

        public void With(
            Type type, Func<ISerializer, Stream, CancellationToken, Task<object>> transformStream,
            Func<ISerializer, DbDataReader, int, CancellationToken, Task<object>> transformDbDataReader
        ) =>
            _jsonTransformations.Register(type, transformStream, transformDbDataReader);
    }
}
