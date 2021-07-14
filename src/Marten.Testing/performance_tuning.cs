using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jil;
using Marten.Services;
using Marten.Util;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Testing
{
    #region sample_JilSerializer
    public class JilSerializer : ISerializer
    {
        private readonly Options _options
            = new(dateFormat: DateTimeFormat.ISO8601, includeInherited:true);

        public ValueCasting ValueCasting { get; } = ValueCasting.Strict;

        public string ToJson(object document)
        {
            return JSON.Serialize(document, _options);
        }

        public T FromJson<T>(Stream stream)
        {
            return JSON.Deserialize<T>(stream.GetStreamReader(), _options);
        }

        public T FromJson<T>(DbDataReader reader, int index)
        {
            var stream = reader.GetStream(index);
            return FromJson<T>(stream);
        }

        public ValueTask<T> FromJsonAsync<T>(Stream stream, CancellationToken cancellationToken = default)
        {
            return new(FromJson<T>(stream));
        }

        public ValueTask<T> FromJsonAsync<T>(DbDataReader reader, int index, CancellationToken cancellationToken = default)
        {
            return new (FromJson<T>(reader, index));
        }

        public object FromJson(Type type, Stream stream)
        {
            return JSON.Deserialize(stream.GetStreamReader(), type, _options);
        }

        public object FromJson(Type type, DbDataReader reader, int index)
        {
            var stream = reader.GetStream(index);
            return FromJson(type, stream);
        }

        public ValueTask<object> FromJsonAsync(Type type, Stream stream, CancellationToken cancellationToken = default)
        {
            return new (FromJson(type, stream));
        }

        public ValueTask<object> FromJsonAsync(Type type, DbDataReader reader, int index, CancellationToken cancellationToken = default)
        {
            return new (FromJson(type, reader, index));
        }

        public string ToCleanJson(object document)
        {
            return ToJson(document);
        }

        public EnumStorage EnumStorage => EnumStorage.AsString;
        public Casing Casing => Casing.Default;
        public string ToJsonWithTypes(object document)
        {
            throw new NotSupportedException();
        }
    }
    #endregion sample_JilSerializer

    public class TestsSerializer : JsonNetSerializer
    {

    }

    public static class JilSamples
    {
        public static void Build_With_Jil()
        {
            #region sample_replacing_serializer_with_jil
            var store = DocumentStore.For(_ =>
            {
                _.Connection("the connection string");

                // Replace the ISerializer w/ the TestsSerializer
                _.Serializer<TestsSerializer>();
            });
            #endregion sample_replacing_serializer_with_jil
        }

    }
}
