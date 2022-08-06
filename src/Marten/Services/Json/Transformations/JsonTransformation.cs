#nullable enable
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Services.Json.Transformations
{
    public class JsonTransformation
    {
        public Func<ISerializer, DbDataReader, int, object> FromDbDataReader { get; }
        public Func<ISerializer, DbDataReader, int, CancellationToken, Task<object>> FromDbDataReaderAsync { get; }

        public JsonTransformation(
            Func<ISerializer, DbDataReader, int, object> fromDbDataReader,
            Func<ISerializer, DbDataReader, int, CancellationToken, Task<object>>? transformDbDataReaderAsync = null
        )
        {
            FromDbDataReader = fromDbDataReader;
            FromDbDataReaderAsync =
                transformDbDataReaderAsync ?? ((serializer, reader, index, _) =>
                    Task.FromResult(FromDbDataReader(serializer, reader, index)));
        }
    }
}
