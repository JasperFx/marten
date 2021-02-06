using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jil;
using Marten.Services;

namespace Marten.Testing
{
    // SAMPLE: JilSerializer
    public class JilSerializer : ISerializer
    {
        private readonly Options _options
            = new Options(dateFormat: DateTimeFormat.ISO8601, includeInherited:true);

        public string ToJson(object document)
        {
            return JSON.Serialize(document, _options);
        }

        public T FromJson<T>(DbDataReader reader, int index)
        {
            var stream = reader.GetStream(index);
            return JSON.Deserialize<T>(new StreamReader(stream), _options);
        }

        public ValueTask<T> FromJsonAsync<T>(DbDataReader reader, int index, CancellationToken cancellationToken = default)
        {
            return new (FromJson<T>(reader, index));
        }

        public object FromJson(Type type, DbDataReader reader, int index)
        {
            var stream = reader.GetStream(index);
            return JSON.Deserialize(new StreamReader(stream), type, _options);
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
        public CollectionStorage CollectionStorage => CollectionStorage.Default;
        public NonPublicMembersStorage NonPublicMembersStorage => NonPublicMembersStorage.Default;
        public string ToJsonWithTypes(object document)
        {
            throw new NotSupportedException();
        }
    }
    // ENDSAMPLE

    public class TestsSerializer : JsonNetSerializer
    {

    }


    public static class JilSamples
    {
        public static void Build_With_Jil()
        {
            // SAMPLE: replacing_serializer_with_jil
            var store = DocumentStore.For(_ =>
            {
                _.Connection("the connection string");

                // Replace the ISerializer w/ the TestsSerializer
                _.Serializer<TestsSerializer>();
            });
            // ENDSAMPLE
        }

    }



}
