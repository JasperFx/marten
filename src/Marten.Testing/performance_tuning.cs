using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Baseline;
using Jil;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;
using Marten.Testing.Documents;

namespace Marten.Testing
{
    // SAMPLE: JilSerializer
    public class JilSerializer : ISerializer
    {
        private readonly Options _options
            = new Options(dateFormat: DateTimeFormat.ISO8601, includeInherited:true);

        public void ToJson(object document, TextWriter writer)
        {
            JSON.Serialize(document, writer, _options);
        }

        public string ToJson(object document)
        {
            return JSON.Serialize(document, _options);
        }

        public T FromJson<T>(TextReader reader)
        {
            return JSON.Deserialize<T>(reader, _options);
        }

        public object FromJson(Type type, TextReader reader)
        {
            return JSON.Deserialize(reader, type, _options);
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
