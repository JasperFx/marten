using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Weasel.Core;

namespace Marten.Services.Json
{
    public class JsonTransformations
    {
        private readonly Cache<Type, Func<ISerializer, Stream, object>> _streamTransformations = new();
        private readonly Cache<Type, Func<ISerializer, Stream, Task<object>>> _streamAsyncTransformations = new();

        private readonly Cache<Type, Func<ISerializer, DbDataReader, int, object>> _dbDataReaderTransformations = new();
        private readonly Cache<Type, Func<ISerializer, DbDataReader, int, Task<object>>>
            _dbDataReaderAsyncTransformations = new();

        public bool TryTransform(ISerializer serializer, Type type, Stream stream, out object? result)
        {
            if (!_streamTransformations.TryFind(type, out var transform))
            {
                result = null;
                return false;
            }

            result = transform(serializer, stream);
            return true;
        }

        public async Task<(bool, object?)> TryTransformAsync(ISerializer serializer, Type type, Stream stream, CancellationToken ct)
        {
            return _streamAsyncTransformations.TryFind(type, out var transform)
                ? (true, await transform(serializer, stream))
                : (false, null);
        }

        public bool TryTransform(ISerializer serializer, Type type, DbDataReader dbDataReader, int index, out object? result)
        {
            if (!_dbDataReaderTransformations.TryFind(type, out var transform))
            {
                result = null;
                return false;
            }

            result = transform(serializer, dbDataReader, index);
            return true;
        }

        public async Task<(bool, object?)> TryTransformAsync(ISerializer serializer, Type type, DbDataReader dbDataReader, int index,
            CancellationToken ct)
        {
            return _dbDataReaderAsyncTransformations.TryFind(type, out var transform)
                ? (true, await transform(serializer, dbDataReader, index))
                : (false, null);
        }

        public JsonTransformations Register(Type type, Func<ISerializer, Stream, object> transform)
        {
            _streamTransformations.Fill(type, transform);
            return this;
        }

        public JsonTransformations Register(Type type, Func<ISerializer, Stream, Task<object>> transform)
        {
            _streamTransformations.Fill(type, transform);
            return this;
        }

        public JsonTransformations Register(Type type, Func<ISerializer, DbDataReader, int, object> transform)
        {
            _dbDataReaderTransformations.Fill(type, transform);
            return this;
        }

        public JsonTransformations Register(Type type, Func<ISerializer, DbDataReader, int, Task<object>> transform)
        {
            _dbDataReaderAsyncTransformations.Fill(type, transform);
            return this;
        }
    }

    public class JsonTransformationSerializerWrapper: ISerializer
    {
        private readonly ISerializer _serializer;
        private readonly JsonTransformations _jsonTransformations;

        public JsonTransformationSerializerWrapper(ISerializer serializer, JsonTransformations jsonTransformations)
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

        public async ValueTask<T> FromJsonAsync<T>(Stream stream, CancellationToken cancellationToken = default)
        {
            var (wasTransformed, result) =
                await _jsonTransformations.TryTransformAsync(_serializer, typeof(T), stream, cancellationToken);

            return wasTransformed
                ? (T)result
                : await _serializer.FromJsonAsync<T>(stream, cancellationToken);
        }

        public async ValueTask<object> FromJsonAsync(Type type, Stream stream,
            CancellationToken cancellationToken = default)
        {
            var (wasTransformed, result) =
                await _jsonTransformations.TryTransformAsync(_serializer, type, stream, cancellationToken);

            return wasTransformed
                ? result
                : await _serializer.FromJsonAsync(type, stream, cancellationToken);
        }

        public T FromJson<T>(DbDataReader reader, int index) =>
            _jsonTransformations.TryTransform(_serializer, typeof(T), reader, index, out var result)
                ? (T)result
                : _serializer.FromJson<T>(reader, index);

        public object FromJson(Type type, DbDataReader reader, int index) =>
            _jsonTransformations.TryTransform(_serializer, type, reader, index, out var result)
                ? result
                : _serializer.FromJson(type, reader, index);


        public async ValueTask<T> FromJsonAsync<T>(DbDataReader reader, int index,
            CancellationToken cancellationToken = default)
        {
            var (wasTransformed, result) =
                await _jsonTransformations.TryTransformAsync(_serializer, typeof(T), reader, index, cancellationToken);

            return wasTransformed
                ? (T)result
                : await _serializer.FromJsonAsync<T>(reader, index, cancellationToken);
        }

        public async ValueTask<object> FromJsonAsync(Type type, DbDataReader reader, int index,
            CancellationToken cancellationToken = default)
        {
            var (wasTransformed, result) =
                await _jsonTransformations.TryTransformAsync(_serializer, type, reader, index, cancellationToken);

            return wasTransformed
                ? result
                : await _serializer.FromJsonAsync(type, reader, index, cancellationToken);
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
