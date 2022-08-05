using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Weasel.Core;

namespace Marten.Services.Json
{
    public class JsonTransformation
    {
        public Func<ISerializer, Stream, object> TransformStream { get; }
        public Func<ISerializer, DbDataReader, int, object> TransformTransformDbDataReader { get; }
        public Func<ISerializer, Stream, CancellationToken, Task<object>> TransformStreamAsync { get; }
        public Func<ISerializer, DbDataReader, int, CancellationToken, Task<object>> TransformDbDataReaderAsync { get; }

        public JsonTransformation(
            Func<ISerializer, Stream, object> transformStream,
            Func<ISerializer, DbDataReader, int, object> transformDbDataReader,
            Func<ISerializer, Stream, CancellationToken, Task<object>>? transformStreamAsync = null,
            Func<ISerializer, DbDataReader, int, CancellationToken, Task<object>>? transformDbDataReaderAsync = null
        )
        {
            TransformStream = transformStream;
            TransformTransformDbDataReader = transformDbDataReader;
            TransformStreamAsync =
                transformStreamAsync ??
                ((serializer, stream, _) => Task.FromResult(TransformStream(serializer, stream)));
            TransformDbDataReaderAsync =
                transformDbDataReaderAsync ?? ((serializer, reader, index, _) =>
                    Task.FromResult(TransformTransformDbDataReader(serializer, reader, index)));
        }
    }

    public class JsonTransformations
    {
        private readonly Cache<Type, JsonTransformation> _transformations = new();

        public bool TryTransform(ISerializer serializer, Type type, Stream stream, out object? result)
        {
            if (!_transformations.TryFind(type, out var transform))
            {
                result = null;
                return false;
            }

            result = transform.TransformStream(serializer, stream);
            return true;
        }

        public async Task<(bool, object?)> TryTransformAsync(ISerializer serializer, Type type, Stream stream,
            CancellationToken ct)
        {
            return _transformations.TryFind(type, out var transform)
                ? (true, await transform.TransformStreamAsync(serializer, stream, ct).ConfigureAwait(false))
                : (false, null);
        }

        public bool TryTransform(ISerializer serializer, Type type, DbDataReader dbDataReader, int index,
            out object? result)
        {
            if (!_transformations.TryFind(type, out var transform))
            {
                result = null;
                return false;
            }

            result = transform.TransformTransformDbDataReader(serializer, dbDataReader, index);
            return true;
        }

        public async Task<(bool, object?)> TryTransformAsync(ISerializer serializer, Type type,
            DbDataReader dbDataReader, int index,
            CancellationToken ct)
        {
            return _transformations.TryFind(type, out var transform)
                ? (true,
                    await transform.TransformDbDataReaderAsync(serializer, dbDataReader, index, ct)
                        .ConfigureAwait(false))
                : (false, null);
        }

        public JsonTransformations Register(Type type, JsonTransformation transformation)
        {
            _transformations.Fill(type, transformation);
            return this;
        }
    }

    internal class JsonTransformationSerializerWrapper: ISerializer
    {
        private readonly ISerializer _serializer;
        private readonly JsonTransformations _jsonTransformations;

        internal JsonTransformationSerializerWrapper(ISerializer serializer, JsonTransformations jsonTransformations)
        {
            _serializer = serializer;
            _jsonTransformations = jsonTransformations;
        }

        public T FromJson<T>(Stream stream) =>
            _jsonTransformations.TryTransform(_serializer, typeof(T), stream, out var result)
                ? (T)result
                : _serializer.FromJson<T>(stream);

        public object FromJson(Type type, Stream stream) =>
            _jsonTransformations.TryTransform(_serializer, type, stream, out var result)
                ? result
                : _serializer.FromJson(type, stream);

        public async ValueTask<T> FromJsonAsync<T>(Stream stream, CancellationToken ct = default)
        {
            var (wasTransformed, result) =
                await _jsonTransformations.TryTransformAsync(_serializer, typeof(T), stream, ct).ConfigureAwait(false);

            return wasTransformed
                ? (T)result
                : await _serializer.FromJsonAsync<T>(stream, ct).ConfigureAwait(false);
        }

        public async ValueTask<object> FromJsonAsync(Type type, Stream stream, CancellationToken ct = default)
        {
            var (wasTransformed, result) =
                await _jsonTransformations.TryTransformAsync(_serializer, type, stream, ct).ConfigureAwait(false);

            return wasTransformed
                ? result
                : await _serializer.FromJsonAsync(type, stream, ct).ConfigureAwait(false);
        }

        public T FromJson<T>(DbDataReader reader, int index) =>
            _jsonTransformations.TryTransform(_serializer, typeof(T), reader, index, out var result)
                ? (T)result
                : _serializer.FromJson<T>(reader, index);

        public object FromJson(Type type, DbDataReader reader, int index) =>
            _jsonTransformations.TryTransform(_serializer, type, reader, index, out var result)
                ? result
                : _serializer.FromJson(type, reader, index);


        public async ValueTask<T> FromJsonAsync<T>(DbDataReader reader, int index, CancellationToken ct = default)
        {
            var (wasTransformed, result) =
                await _jsonTransformations.TryTransformAsync(_serializer, typeof(T), reader, index, ct)
                    .ConfigureAwait(false);

            return wasTransformed
                ? (T)result
                : await _serializer.FromJsonAsync<T>(reader, index, ct).ConfigureAwait(false);
        }

        public async ValueTask<object> FromJsonAsync(Type type, DbDataReader reader, int index,
            CancellationToken ct = default)
        {
            var (wasTransformed, result) =
                await _jsonTransformations.TryTransformAsync(_serializer, type, reader, index, ct)
                    .ConfigureAwait(false);

            return wasTransformed
                ? result
                : await _serializer.FromJsonAsync(type, reader, index, ct).ConfigureAwait(false);
        }

        public string ToJson(object document) =>
            _serializer.ToJson(document);

        public string ToCleanJson(object document) =>
            _serializer.ToCleanJson(document);

        public string ToJsonWithTypes(object document) =>
            _serializer.ToJsonWithTypes(document);

        public EnumStorage EnumStorage => _serializer.EnumStorage;
        public Casing Casing => _serializer.Casing;
        public ValueCasting ValueCasting => _serializer.ValueCasting;
    }
}
