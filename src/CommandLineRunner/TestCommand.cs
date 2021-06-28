using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Events.Aggregation;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Oakton;
using Shouldly;

namespace CommandLineRunner
{
    public class TestCommand : OaktonAsyncCommand<NetCoreInput>
    {
        public override async Task<bool> Execute(NetCoreInput input)
        {
            using var host = input.BuildHost();

            var store = host.Services.GetRequiredService<IDocumentStore>();
            await store.Advanced.Clean.DeleteAllDocumentsAsync();

            var targets = Target.GenerateRandomData(1000).ToArray();

            // Bulk Insert
            Console.WriteLine("Bulk Writing");
            await store.BulkInsertDocumentsAsync(targets);

            Console.WriteLine("QueryOnly");
            using (var session1 = store.QuerySession())
            {
                (await session1.Query<Target>().Take(1).ToListAsync()).Single().ShouldBeOfType<Target>();
            }

            Console.WriteLine("Lightweight");
            using (var session2 = store.LightweightSession())
            {
                var target = Target.Random();
                session2.Store(target);
                await session2.SaveChangesAsync();
            }

            Console.WriteLine("IdentityMap");
            using (var session3 = store.OpenSession())
            {
                var target = Target.Random();
                session3.Store(target);
                await session3.SaveChangesAsync();
            }

            Console.WriteLine("DirtyChecking");
            using (var session4 = store.OpenSession())
            {
                var target = Target.Random();
                session4.Store(target);
                await session4.SaveChangesAsync();
            }

            Console.WriteLine("Capturing Events");
            using (var session = store.LightweightSession())
            {
                var streamId = Guid.NewGuid();
                session.Events.Append(streamId, new AEvent(), new BEvent(), new CEvent(), new DEvent());
                await session.SaveChangesAsync();

                (await session.Events.AggregateStreamAsync<MyAggregate>(streamId)).ShouldBeOfType<MyAggregate>();

                var events = await session.Events.FetchStreamAsync(streamId);
                events.Count.ShouldBe(4);

                var aggregate = await session.LoadAsync<MyAggregate>(streamId);
                aggregate.ShouldNotBeNull();
            }

            ConsoleWriter.Write(ConsoleColor.Green, "All Good!");

            return true;
        }
    }
}
