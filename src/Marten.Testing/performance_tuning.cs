using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Jil;
using Marten.Schema;
using Marten.Testing.Fixtures;
using NpgsqlTypes;
using StructureMap;

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
    }


    public class performance_measurements
    {
        public class DateIsSearchable : MartenRegistry
        {
            public DateIsSearchable()
            {
                For<Target>().Searchable(x => x.Date);
            }
        }

        public class JsonLocatorOnly : MartenRegistry
        {
            public JsonLocatorOnly()
            {
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
                session.BulkLoad(data);

                var theDate = DateTime.Today.AddDays(3);
                var queryable = session.Query<Target>().Where(x => x.Date == theDate);

                Debug.WriteLine(session.Diagnostics.CommandFor(queryable).CommandText);

                var time = Timings.Time(() =>
                {
                    queryable.ToArray().Length.ShouldBeGreaterThan(0);
                });

                var description =
                    $"{data.Length} documents / {typeof (TSerializer).Name} / {typeof (TRegistry).Name}: {time}";

                Debug.WriteLine(description);

            }
        }

        public void measure_for_1000_documents()
        {
            Target[] data = Target.GenerateRandomData(1000).ToArray();

            Debug.WriteLine("Newtonsoft Serializer, 1,000 documents");

            time_query<JsonNetSerializer, JsonLocatorOnly>(data);
            time_query<JsonNetSerializer, JsonBToRecord>(data);
            time_query<JsonNetSerializer, DateIsSearchable>(data);
            time_query<JsonNetSerializer, JsonLocatorOnly>(data);
            time_query<JsonNetSerializer, JsonBToRecord>(data);
            time_query<JsonNetSerializer, DateIsSearchable>(data);
            Debug.WriteLine("");

            Debug.WriteLine("Newtonsoft Serializer, 10,000 documents");
            data = Target.GenerateRandomData(10000).ToArray();
            time_query<JsonNetSerializer, JsonLocatorOnly>(data);
            time_query<JsonNetSerializer, JsonBToRecord>(data);
            time_query<JsonNetSerializer, DateIsSearchable>(data);
            time_query<JsonNetSerializer, JsonLocatorOnly>(data);
            time_query<JsonNetSerializer, JsonBToRecord>(data);
            time_query<JsonNetSerializer, DateIsSearchable>(data);
            Debug.WriteLine("");


            return;
            Debug.WriteLine("Newtonsoft Serializer, 100,000 documents");
            data = Target.GenerateRandomData(100000).ToArray();
            time_query<JsonNetSerializer, JsonBToRecord>(data);
            time_query<JsonNetSerializer, JsonLocatorOnly>(data);
            time_query<JsonNetSerializer, DateIsSearchable>(data);
            Debug.WriteLine("");

            Debug.WriteLine("Newtonsoft Serializer, 1M documents");
            data = Target.GenerateRandomData(1000000).ToArray();
            time_query<JsonNetSerializer, JsonBToRecord>(data);
            time_query<JsonNetSerializer, JsonLocatorOnly>(data);
            time_query<JsonNetSerializer, DateIsSearchable>(data);
            Debug.WriteLine("");

            Debug.WriteLine("Jil Serializer, 1,000 documents");
            data = Target.GenerateRandomData(1000).ToArray();
            time_query<JilSerializer, JsonBToRecord>(data);
            time_query<JilSerializer, JsonLocatorOnly>(data);
            time_query<JilSerializer, DateIsSearchable>(data);
            Debug.WriteLine("");

            Debug.WriteLine("Jil Serializer, 10,000 documents");
            data = Target.GenerateRandomData(10000).ToArray();
            time_query<JilSerializer, JsonBToRecord>(data);
            time_query<JilSerializer, JsonLocatorOnly>(data);
            time_query<JilSerializer, DateIsSearchable>(data);
            Debug.WriteLine("");

            Debug.WriteLine("Jil Serializer, 100,000 documents");
            data = Target.GenerateRandomData(100000).ToArray();
            time_query<JilSerializer, JsonBToRecord>(data);
            time_query<JilSerializer, JsonLocatorOnly>(data);
            time_query<JilSerializer, DateIsSearchable>(data);
            Debug.WriteLine("");

            Debug.WriteLine("Jil Serializer, 1M documents");
            data = Target.GenerateRandomData(1000000).ToArray();
            time_query<JilSerializer, JsonBToRecord>(data);
            time_query<JilSerializer, JsonLocatorOnly>(data);
            time_query<JilSerializer, DateIsSearchable>(data);
            Debug.WriteLine("");

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
                session.BulkLoad(data);
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