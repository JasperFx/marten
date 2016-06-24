using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Baseline;
using HtmlTags;
using Marten.Schema;
using Marten.Services;
using Marten.Testing.Fixtures;
using StructureMap;
using StructureMap.Util;
using Xunit;

namespace Marten.Testing
{
    public class insert_timing
    {
        private string key<TSerializer, TRegistry, TStrategy>()
        {
            return $"{typeof(TSerializer).Name}-{typeof(TRegistry).Name}-{typeof(TStrategy).Name}";
        }

        private readonly Dictionary<string, double> _timings = new Dictionary<string, double>(); 


        private double timeFor<TSerializer, TRegistry, TStrategy>()
        {
            return _timings[key<TSerializer, TRegistry, TStrategy>()];
        }

        private void store<TSerializer, TRegistry, TStrategy>(double time)
        {
            var key = key<TSerializer, TRegistry, TStrategy>();
            _timings.Add(key, time);
        }


        private void create_timing<TSerializer, TRegistry, TStrategy>(Target[] data)
            where TSerializer : ISerializer
            where TRegistry : MartenRegistry, new()
            where TStrategy : InsertStrategy, new()
        {
            var container = Container.For<DevelopmentModeRegistry>();
            container.Configure(x => x.For<ISerializer>().Use<TSerializer>());

            container.GetInstance<IDocumentCleaner>().CompletelyRemoveAll();

            var schema = container.GetInstance<IDocumentSchema>();
throw new NotImplementedException("No longer have an Alter() mechanism to add the MartenRegistry");
            /*
            schema.EnsureStorageExists(typeof(Target));

            var strategy = new TStrategy();

            // Do it twice, throw out the first time
            var time = strategy.Insert(container, data);

            data.Each(x => x.Id = Guid.NewGuid());
            time = strategy.Insert(container, data);

            store<TSerializer, TRegistry, TStrategy>(time);

            Debug.WriteLine(key<TSerializer, TRegistry, TStrategy>() + ": " + time);
            */
        }


        //[Fact]
        public void run_all_inserts()
        {
            var data = Target.GenerateRandomData(500).ToArray();

            create_timing<JsonNetSerializer, NoIndexes, BulkInserts>(data);
            create_timing<JsonNetSerializer, NoIndexes, BatchUpdates>(data);
            create_timing<JsonNetSerializer, NoIndexes, MultipleCommands>(data);
            
            
            create_timing<JsonNetSerializer, IndexedDuplicatedField, BulkInserts>(data);
            create_timing<JsonNetSerializer, IndexedDuplicatedField, BatchUpdates>(data);
            create_timing<JsonNetSerializer, IndexedDuplicatedField, MultipleCommands>(data);

            create_timing<JsonNetSerializer, WithGin, BulkInserts>(data);
            create_timing<JsonNetSerializer, WithGin, BatchUpdates>(data);
            create_timing<JsonNetSerializer, WithGin, MultipleCommands>(data);

            create_timing<JilSerializer, NoIndexes, BulkInserts>(data);
            create_timing<JilSerializer, NoIndexes, BatchUpdates>(data);
            create_timing<JilSerializer, NoIndexes, MultipleCommands>(data);

            create_timing<JilSerializer, IndexedDuplicatedField, BulkInserts>(data);
            create_timing<JilSerializer, IndexedDuplicatedField, BatchUpdates>(data);
            create_timing<JilSerializer, IndexedDuplicatedField, MultipleCommands>(data);

            create_timing<JilSerializer, WithGin, BulkInserts>(data);
            create_timing<JilSerializer, WithGin, BatchUpdates>(data);
            create_timing<JilSerializer, WithGin, MultipleCommands>(data);
            

            var document = new HtmlDocument();
            document.Title = "Insert Timings";

            document.Add("h2").Text("Newtonsoft.Json");
            document.Add(tableForSerializer<JsonNetSerializer>());
            document.Add("h2").Text("Jil");
            document.Add(tableForSerializer<JilSerializer>());

            document.OpenInBrowser();
        }

        private HtmlTag tableForSerializer<T>()
        {
            var table = new TableTag();

            table.AddHeaderRow(row =>
            {
                row.Header("Index");
                row.Header("Bulk Insert");
                row.Header("Batch Update");
                row.Header("Command per Document");
            });

            table.AddBodyRow(row =>
            {
                row.Cell("No Index");
                row.Cell(timeFor<T, NoIndexes, BulkInserts>().ToString()).Style("align", "right");
                row.Cell(timeFor<T, NoIndexes, BatchUpdates>().ToString()).Style("align", "right");
                row.Cell(timeFor<T, NoIndexes, MultipleCommands>().ToString()).Style("align", "right");
            });

            table.AddBodyRow(row =>
            {
                row.Cell("Duplicated Field w/ Index");
                row.Cell(timeFor<T, IndexedDuplicatedField, BulkInserts>().ToString()).Style("align", "right");
                row.Cell(timeFor<T, IndexedDuplicatedField, BatchUpdates>().ToString()).Style("align", "right");
                row.Cell(timeFor<T, IndexedDuplicatedField, MultipleCommands>().ToString()).Style("align", "right");
            });

            table.AddBodyRow(row =>
            {
                row.Cell("Gin Index on Json");
                row.Cell(timeFor<T, WithGin, BulkInserts>().ToString()).Style("align", "right");
                row.Cell(timeFor<T, WithGin, BatchUpdates>().ToString()).Style("align", "right");
                row.Cell(timeFor<T, WithGin, MultipleCommands>().ToString()).Style("align", "right");
            });

            return table;
        }
    }

    

    public interface InsertStrategy
    {
        double Insert(IContainer container, Target[] data);
    }

    public class WithGin : MartenRegistry
    {
        public WithGin()
        {
            For<Target>().GinIndexJsonData();
        }
    }

    public class NoIndexes : MartenRegistry
    {
        
    }

    public class IndexedDuplicatedField : MartenRegistry
    {
        public IndexedDuplicatedField()
        {
            For<Target>().Duplicate(x => x.Date);
        }
    }

    public class BulkInserts : InsertStrategy
    {
        public double Insert(IContainer container, Target[] data)
        {
            var store = container.GetInstance<IDocumentStore>();

            return Timings.Time(() => store.BulkInsert(data));
        }
    }

    public class BatchUpdates : InsertStrategy
    {
        public double Insert(IContainer container, Target[] data)
        {
            var batch = container.GetInstance<UpdateBatch>();

            var unitofwork = container.GetInstance<UnitOfWork>();

            return Timings.Time(() =>
            {
                unitofwork.StoreUpdates(data);
                unitofwork.ApplyChanges(batch);
            });
        }
    }

    public class MultipleCommands : InsertStrategy
    {
        public double Insert(IContainer container, Target[] data)
        {
            var schema = container.GetInstance<IDocumentSchema>();
            var runner = container.GetInstance<IManagedConnection>();
            var storage = schema.StorageFor(typeof (Target));
            var serializer = container.GetInstance<ISerializer>();

            return Timings.Time(() =>
            {
//                connection.ExecuteInTransaction((conn, tx) =>
//                {
//                    data.Each(t =>
//                    {
//                        throw new NotSupportedException("This mechanism is no long supported");
//                        //var cmd = storage.UpsertCommand(t, serializer.ToJson(t));
//                        //cmd.Connection = conn;
//                        //cmd.Transaction = tx;
//                        //cmd.ExecuteNonQuery();
//                    });
//                });
            });
        }
    }
}