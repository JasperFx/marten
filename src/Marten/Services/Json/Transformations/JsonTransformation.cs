using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Services.Json
{
    public class JsonTransformation
    {
        public Func<ISerializer, DbDataReader, int, object> TransformDbDataReader { get; }
        public Func<ISerializer, DbDataReader, int, CancellationToken, Task<object>> TransformDbDataReaderAsync { get; }

        public JsonTransformation(
            Func<ISerializer, DbDataReader, int, object> transformDbDataReader,
            Func<ISerializer, DbDataReader, int, CancellationToken, Task<object>>? transformDbDataReaderAsync = null
        )
        {
            TransformDbDataReader = transformDbDataReader;
            TransformDbDataReaderAsync =
                transformDbDataReaderAsync ?? ((serializer, reader, index, _) =>
                    Task.FromResult(TransformDbDataReader(serializer, reader, index)));
        }
    }
}
