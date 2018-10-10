using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;
#if NET46
using HtmlTags;
using Jil;
#endif

namespace Marten.Testing
{
#if NET46
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

        public T FromJson<T>(string json)
        {
            return JSON.Deserialize<T>(json, _options);
        }

        public T FromJson<T>(Stream stream)
        {
            return JSON.Deserialize<T>(new StreamReader(stream), _options);
        }

        public object FromJson(Type type, string json)
        {
            return JSON.Deserialize(json, type, _options);
        }

        public T FromJson<T>(TextReader reader)
        {
            return JSON.Deserialize<T>(reader);
        }

        public object FromJson(Type type, TextReader reader)
        {
            return JSON.Deserialize(reader, type);
        }

        public string ToCleanJson(object document)
        {
            return ToJson(document);
        }

        public EnumStorage EnumStorage => EnumStorage.AsString;
        public Casing Casing => Casing.Default;
    }
    // ENDSAMPLE
#endif

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


        public static void custom_newtonsoft()
        {
            
        }
    }



    public class performance_measurements
    {
        public class SerializerTiming
        {
            public readonly LightweightCache<Type, Dictionary<int, double>> Timings
                = new LightweightCache<Type, Dictionary<int, double>>(x => new Dictionary<int, double>());

            public void Record<T>(int count, double average)
            {
                Timings[typeof(T)].Add(count, average);
            }
        }

        private readonly LightweightCache<Type, SerializerTiming> _timings =
            new LightweightCache<Type, SerializerTiming>(t => new SerializerTiming());

        public void time_query<TSerializer, TRegistry>(Target[] data)
            where TSerializer : ISerializer
            where TRegistry : MartenRegistry, new()
        {

            // Apply the schema customizations
            //container.GetInstance<IDocumentSchema>().Alter<TRegistry>();
            throw new NotImplementedException("See line of code above, no longer valid");
            /*
            using (var session = container.GetInstance<IDocumentStore>().OpenSession())
            {
                var store = container.GetInstance<IDocumentStore>();

                store.BulkInsert(data);

                var theDate = data.ElementAt(0).Date;
                var queryable = session.Query<Target>().Where(x => x.Date == theDate);

                Debug.WriteLine(queryable.ToCommand(FetchType.FetchMany).CommandText);

                // Once to warm up
                var time = Timings.Time(() => { queryable.ToArray().Length.ShouldBeGreaterThan(0); });


                var times = new double[5];
                for (var i = 0; i < 5; i++)
                {
                    times[i] = Timings.Time(() => { queryable.ToArray().Length.ShouldBeGreaterThan(0); });
                }

                var average = times.Average(x => x);

                var description =
                    $"{data.Length} documents / {typeof (TSerializer).Name} / {typeof (TRegistry).Name}: {average}";

                Debug.WriteLine(description);

                _timings[typeof (TSerializer)].Record<TRegistry>(data.Length, average);
            }
            */
        }

        public void time_inserts<TSerializer, TRegistry>(Target[] data)
            where TSerializer : ISerializer
            where TRegistry : MartenRegistry, new()
        {
            // Apply the schema customizations
            //container.GetInstance<IDocumentSchema>().Alter<TRegistry>();
            throw new NotImplementedException("See above line of code");

            /*
            using (var session = container.GetInstance<IDocumentStore>().OpenSession())
            {
                var store = container.GetInstance<IDocumentStore>();

                // Once to warm up
                var time = Timings.Time(() => { store.BulkInsert(data); });

                var description =
                    $"{data.Length} documents / {typeof(TSerializer).Name} / {typeof(TRegistry).Name}: {time}";

                Debug.WriteLine(description);

                _timings[typeof(TSerializer)].Record<TRegistry>(data.Length, time);
            }
            */
        }


        private void create_timings(int length)
        {
            var data = Target.GenerateRandomData(length).ToArray();
            
            time_query<JsonNetSerializer, JsonLocatorOnly>(data);
            time_query<JsonNetSerializer, JsonBToRecord>(data);
            time_query<JsonNetSerializer, DateIsSearchable>(data);
            time_query<JsonNetSerializer, ContainmentOperator>(data);

            time_query<TestsSerializer, JsonLocatorOnly>(data);
            time_query<TestsSerializer, JsonBToRecord>(data);
            time_query<TestsSerializer, DateIsSearchable>(data);
            
            time_query<TestsSerializer, ContainmentOperator>(data);
        }



#if NET46

        private void measure_and_report()
        {
            create_timings(1000);
            create_timings(10000);
            create_timings(100000);
            //create_timings(1000000);

            var document = new HtmlDocument();
            document.Add("h1").Text("Marten Where Timings");

            document.Add(reportForSerializer(typeof (JsonNetSerializer)));
            document.Add(reportForSerializer(typeof (TestsSerializer)));

            document.OpenInBrowser();
        }

        private HtmlTag reportForSerializer(Type serializerType)
        {
            var timing = _timings[serializerType];

            var div = new HtmlTag("div");

            div.Add("h3").Text("Serializer: " + serializerType.Name);

            var table = new TableTag();
            div.Append(table);

            table.AddHeaderRow(tr =>
            {
                tr.Header("Where Type");
                tr.Header("1K");
                tr.Header("10K");
                tr.Header("100K");
                //tr.Header("1M");
            });

            table.AddBodyRow(tr =>
            {
                tr.Header("JSON Locator Only");

                var dict = timing.Timings[typeof (JsonLocatorOnly)];

                tr.Cell(dict[1000].ToString());
                tr.Cell(dict[10000].ToString());
                tr.Cell(dict[100000].ToString());
                //tr.Cell(dict[1000000].ToString());
            });

            table.AddBodyRow(tr =>
            {
                tr.Header("jsonb_to_record + lateral join");

                var dict = timing.Timings[typeof (JsonBToRecord)];

                tr.Cell(dict[1000].ToString());
                tr.Cell(dict[10000].ToString());
                tr.Cell(dict[100000].ToString());
                //tr.Cell(dict[1000000].ToString());
            });

            table.AddBodyRow(tr =>
            {
                tr.Header("searching by duplicated field");

                var dict = timing.Timings[typeof (DateIsSearchable)];

                tr.Cell(dict[1000].ToString());
                tr.Cell(dict[10000].ToString());
                tr.Cell(dict[100000].ToString());
                //tr.Cell(dict[1000000].ToString());
            });

            table.AddBodyRow(tr =>
            {
                tr.Header("searching by containment operator");

                var dict = timing.Timings[typeof(ContainmentOperator)];

                tr.Cell(dict[1000].ToString());
                tr.Cell(dict[10000].ToString());
                tr.Cell(dict[100000].ToString());
                //tr.Cell(dict[1000000].ToString());
            });

            return div;
        }

#endif

public class DateIsSearchable : MartenRegistry
{
    public DateIsSearchable()
    {
        // This can also be done with attributes
        // This automatically adds a "BTree" index
        For<Target>().Duplicate(x => x.Date);
    }
}

        public class JsonLocatorOnly : MartenRegistry
        {
            public JsonLocatorOnly()
            {
                // This can also be done with attributes
                For<Target>().GinIndexJsonData().PropertySearching(PropertySearching.JSON_Locator_Only);
            }
        }

public class ContainmentOperator : MartenRegistry
{
    public ContainmentOperator()
    {
        // For persisting a document type called 'Target'
        For<Target>()

            // Use a gin index against the json data field
            .GinIndexJsonData()

            // directs Marten to try to use the containment
            // operator for querying against this document type
            // in the Linq support
            .PropertySearching(PropertySearching.ContainmentOperator);
    }
}

        public class JsonBToRecord : MartenRegistry
        {
            public JsonBToRecord()
            {
                For<Target>().GinIndexJsonData();
            }
        }
    }


    public class performance_tuning
    {



    }

    public static class Timings
    {
        public static double Time(Action action)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            try
            {
                action();
            }
            finally
            {
                stopwatch.Stop();
            }

            return stopwatch.ElapsedMilliseconds;
        }
    }



}