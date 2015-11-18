using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HtmlTags;
using Jil;
using Marten.Schema;
using Marten.Testing.Fixtures;
using NpgsqlTypes;
using StructureMap;
using StructureMap.Util;

namespace Marten.Testing
{
    public class JilSerializer : ISerializer
    {
        public string ToJson(object document)
        {
            return Jil.JSON.Serialize(document, new Options(dateFormat:DateTimeFormat.ISO8601));
        }

        public T FromJson<T>(string json)
        {
            return Jil.JSON.Deserialize<T>(json, new Options(dateFormat: DateTimeFormat.ISO8601));
        }

        public T FromJson<T>(Stream stream)
        {
            return Jil.JSON.Deserialize<T>(new StreamReader(stream), new Options(dateFormat: DateTimeFormat.ISO8601));
        }

        public object FromJson(Type type, string json)
        {
            return Jil.JSON.Deserialize(json, type, new Options(dateFormat: DateTimeFormat.ISO8601));
        }
    }



    public class SerializerTiming
    {
        public readonly LightweightCache<Type, Dictionary<int, double>> Timings 
            = new LightweightCache<Type, Dictionary<int, double>>(x => new Dictionary<int, double>());

        public void Record<T>(int count, double average)
        {
            Timings[typeof(T)].Add(count, average);            
        }
    }


    public class performance_measurements
    {
        private readonly LightweightCache<Type, SerializerTiming> _timings = new LightweightCache<Type, SerializerTiming>(t => new SerializerTiming()); 
         

public class DateIsSearchable : MartenRegistry
{
    public DateIsSearchable()
    {
        // This can also be done with attributes
        For<Target>().Searchable(x => x.Date);
    }
}

public class JsonLocatorOnly : MartenRegistry
{
    public JsonLocatorOnly()
    {
        // This can also be done with attributes
        For<Target>().PropertySearching(PropertySearching.JSON_Locator_Only);
    }
}

        public class JsonBToRecord : MartenRegistry
        {
            public JsonBToRecord()
            {
                For<Target>().PropertySearching(PropertySearching.JSONB_To_Record);
            }
        }

        public void time_query<TSerializer, TRegistry>(Target[] data)
            where TSerializer : ISerializer
            where TRegistry : MartenRegistry, new()
        {
            var container = Container.For<DevelopmentModeRegistry>();
            container.Configure(_ => _.For<ISerializer>().Use<TSerializer>());


            // Completely removes all the database schema objects for the
            // Target document type
            container.GetInstance<DocumentCleaner>().CompletelyRemoveAll();

            // Apply the schema customizations
            container.GetInstance<IDocumentSchema>().Alter<TRegistry>();


            using (var session = container.GetInstance<IDocumentSession>())
            {
                session.BulkInsert(data);

var theDate = DateTime.Today.AddDays(3);
var queryable = session.Query<Target>().Where(x => x.Date == theDate);

                Debug.WriteLine(session.Diagnostics.CommandFor(queryable).CommandText);

                // Once to warm up
                var time = Timings.Time(() =>
                {
                    queryable.ToArray().Length.ShouldBeGreaterThan(0);
                });


                var times = new double[5];
                for (var i = 0; i < 5; i++)
                {
                    times[i] = Timings.Time(() =>
                    {
                        queryable.ToArray().Length.ShouldBeGreaterThan(0);
                    });
                }

                var average = times.Average(x => x);

                var description =
                    $"{data.Length} documents / {typeof (TSerializer).Name} / {typeof (TRegistry).Name}: {average}";

                Debug.WriteLine(description);

                _timings[typeof(TSerializer)].Record<TRegistry>(data.Length, average);

            }
        }

        private void create_timings(int length)
        {
            Target[] data = Target.GenerateRandomData(length).ToArray();

            time_query<JsonNetSerializer, JsonLocatorOnly>(data);
            time_query<JsonNetSerializer, JsonBToRecord>(data);
            time_query<JsonNetSerializer, DateIsSearchable>(data);

            time_query<JilSerializer, JsonLocatorOnly>(data);
            time_query<JilSerializer, JsonBToRecord>(data);
            time_query<JilSerializer, DateIsSearchable>(data);
        }


        private void measure_and_report()
        {
            create_timings(1000);
            create_timings(10000);
            create_timings(100000);
            create_timings(1000000);

            var document = new HtmlDocument();
            document.Add("h1").Text("Marten Query Timings");

            document.Add(reportForSerializer(typeof (JsonNetSerializer)));
            document.Add(reportForSerializer(typeof (JilSerializer)));

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
                tr.Header("Query Type");
                tr.Header("1K");
                tr.Header("10K");
                tr.Header("100K");
                tr.Header("1M");
            });

            table.AddBodyRow(tr =>
            {
                tr.Header("JSON Locator Only");

                var dict = timing.Timings[typeof (JsonLocatorOnly)];

                tr.Cell(dict[1000].ToString());
                tr.Cell(dict[10000].ToString());
                tr.Cell(dict[100000].ToString());
                tr.Cell(dict[1000000].ToString());
            });

            table.AddBodyRow(tr =>
            {
                tr.Header("jsonb_to_record + lateral join");

                var dict = timing.Timings[typeof(JsonBToRecord)];

                tr.Cell(dict[1000].ToString());
                tr.Cell(dict[10000].ToString());
                tr.Cell(dict[100000].ToString());
                tr.Cell(dict[1000000].ToString());
            });

            table.AddBodyRow(tr =>
            {
                tr.Header("searching by duplicated field");

                var dict = timing.Timings[typeof(DateIsSearchable)];

                tr.Cell(dict[1000].ToString());
                tr.Cell(dict[10000].ToString());
                tr.Cell(dict[100000].ToString());
                tr.Cell(dict[1000000].ToString());
            });

            return div;


        }
    }


    


    public class performance_tuning
    {
        private readonly IContainer theContainer = Container.For<DevelopmentModeRegistry>();







        public void generate_data()
        {
            //theContainer.Inject<ISerializer>(new JilSerializer());

            theContainer.GetInstance<DocumentCleaner>().CompletelyRemove(typeof(Target));

            // Get Roslyn spun up before measuring anything
            var schema = theContainer.GetInstance<IDocumentSchema>();

            schema.MappingFor(typeof (Target)).DuplicateField("Date");

            schema.StorageFor(typeof(Target)).ShouldNotBeNull();

            theContainer.GetInstance<DocumentCleaner>().DeleteDocumentsFor(typeof(Target));


            var session = theContainer.GetInstance<IDocumentSession>();

            var data = Target.GenerateRandomData(10000).ToArray();
            Timings.Time(() =>
            {
                session.BulkInsert(data);
            });
            
            

            




            var theDate = DateTime.Today.AddDays(3);
            
            var one = Timings.Time(() =>
            {
                var sql = "select data from mt_doc_target where (data ->> 'Date')::date = ?";
                session.Query<Target>(sql, theDate).ToArray().Length.ShouldBeGreaterThan(0);
            });
            


            var two = Timings.Time(() =>
            {
                var sql =
                    "select r.data from mt_doc_target as r, LATERAL jsonb_to_record(r.data) as l(\"Date\" date) where l.\"Date\" = ?";

                session.Query<Target>(sql, theDate).ToArray().Count().ShouldBeGreaterThan(0);
            });

            var three = Timings.Time(() =>
            {
                var sql =
                    "select r.data from mt_doc_target as r where r.date = ?";

                session.Query<Target>(sql, theDate).ToArray().Count().ShouldBeGreaterThan(0);
            });

            Debug.WriteLine($"json locator: {one}, lateral join: {two}, searchable field: {three}");
        }
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