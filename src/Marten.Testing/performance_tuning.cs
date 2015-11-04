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

    public class performance_tuning
    {
        private readonly IContainer theContainer = Container.For<DevelopmentModeRegistry>();

        public void generate_data()
        {
            theContainer.Inject<ISerializer>(new JilSerializer());
            //theContainer.Inject<ISerializer>(new NetJSONSerializer());

            theContainer.GetInstance<DocumentCleaner>().CompletelyRemove(typeof(Target));
            // Get Roslyn spun up before measuring anything
            var schema = theContainer.GetInstance<IDocumentSchema>();

            schema.MappingFor(typeof (Target)).DuplicateField("Date");

            schema.StorageFor(typeof(Target)).ShouldNotBeNull();

            theContainer.GetInstance<DocumentCleaner>().DeleteDocumentsFor(typeof(Target));


            var runner = theContainer.GetInstance<CommandRunner>();
            var serializer = theContainer.GetInstance<ISerializer>();
            for (int i = 0; i < 10; i++)
            {
                var data = Target.GenerateRandomData(10000).ToArray();
                Timings.Time("Using BinaryImport", () =>
                {
                    runner.Execute(conn =>
                    {
                        using (var writer = conn.BeginBinaryImport("COPY mt_doc_target (id, data, date) FROM STDIN BINARY"))
                        {
                            data.Each(x =>
                            {
                                writer.StartRow();
                                writer.Write(x.Id, NpgsqlDbType.Uuid);
                                writer.Write(serializer.ToJson(x), NpgsqlDbType.Jsonb);
                                writer.Write(x.Date, NpgsqlDbType.Date);
                            });
                        }
                    });
                });
            }
            

            



            var session = theContainer.GetInstance<IDocumentSession>();





            var theDate = DateTime.Today.AddDays(3);
            
            Timings.Time("Fetching as is", () =>
            {
                session.Query<Target>().Where(x => x.Date == theDate).ToArray().Length.ShouldBeGreaterThan(0);
            });
            

            /*
SELECT r.id 
    FROM resources AS r,
    LATERAL jsonb_to_record(r.fields) AS l(polled integer) 
    WHERE l.polled > 50;
    */

            Timings.Time("Fetching with lateral join", () =>
            {
                var sql =
                    "select r.data from mt_doc_target as r, LATERAL jsonb_to_record(r.data) as l(\"Date\" date) where l.\"Date\" = ?";

                session.Query<Target>(sql, theDate).ToArray().Count().ShouldBeGreaterThan(0);
            });

            Timings.Time("Fetching with duplicated field", () =>
            {
                var sql =
                    "select r.data from mt_doc_target as r where r.date = ?";

                session.Query<Target>(sql, theDate).ToArray().Count().ShouldBeGreaterThan(0);
            });
        }
    }

    public static class Timings
    {
        public static void Time(string description, Action action)
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
                Debug.WriteLine(description + ": " + stopwatch.ElapsedMilliseconds);
            }
        }
    }
}