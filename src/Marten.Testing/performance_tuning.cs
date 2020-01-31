using System;
using System.IO;
using Jil;
using Marten.Services;

namespace Marten.Testing
{
    // SAMPLE: JilSerializer
    public class JilSerializer : ISerializer
    {
        private readonly Options _options
            = new Options(dateFormat: DateTimeFormat.ISO8601, includeInherited:true);

        public void ToJson(object document, Stream stream)
        {
            using var writer = new StreamWriter(stream);
            JSON.Serialize(document, writer, _options);
            writer.Flush();
        }

        public string ToJson(object document)
        {
            return JSON.Serialize(document, _options);
        }

        public T FromJson<T>(Stream stream)
        {
            return JSON.Deserialize<T>(new StreamReader(stream), _options);
        }

        public object FromJson(Type type, Stream stream)
        {
            return JSON.Deserialize(new StreamReader(stream), type, _options);
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
