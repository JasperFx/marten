using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Baseline.Dates;
using Marten;
using Marten.Services;
using Marten.Testing.Harness;
using Weasel.Postgresql;

namespace MemoryUsageChecker
{
    class Program
    {
        static async Task Main(string[] args)
        {

            showProcessData();


            using var store = DocumentStore.For(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.AutoCreateSchemaObjects = AutoCreate.All;
            });

            showProcessData();


            var parentItem = new Parent
            {
                Children = new List<Child>
                {
                    new Child()
                }
            };




            while (true)
            {
                // using (var session = store.OpenSession())
                // {
                //     session.Store(new Target());
                //     session.Store(new Target());
                //     session.Store(new Target());
                //     session.Store(new Target());
                //
                //     session.Events.StartStream(new AEvent(), new BEvent(), new CEvent());
                //
                //     await session.SaveChangesAsync();
                // }

                using (var session = store.OpenSession(SessionOptions.ForCurrentTransaction()))
                {
                    session.Store(parentItem);
                    await session.SaveChangesAsync();
                }

                showProcessData();

                using (var session = store.OpenSession(SessionOptions.ForCurrentTransaction()))
                {
                    await session.Query<Parent>().FirstOrDefaultAsync(p => p.Children.Any(c => c.Id == Guid.Empty));
                }

                showProcessData();

                GC.Collect();

                await Task.Delay(1.Seconds());
            }






        }

        private static void showProcessData()
        {
            var me = Process.GetCurrentProcess();
            Console.WriteLine("Working set {0} bytes", me.WorkingSet64);
            Console.WriteLine("Working set {0} Mb", me.WorkingSet64 / 1024 / 1024);
            Console.WriteLine("Total CPU time {0} sec", me.TotalProcessorTime.TotalSeconds);
        }
    }

    public class Parent
    {
        public Guid Id { get; set; }
        public List<Child> Children { get; set; }
    }

    public class Child
    {
        public Guid Id { get; set; }
    }
}
