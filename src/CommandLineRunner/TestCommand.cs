using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Testing.Documents;
using Marten.Testing.Events.Aggregation;
using Marten.Testing.Harness;
using Marten.Testing.Linq.Compiled;
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

            await store.Schema.ApplyAllConfiguredChangesToDatabaseAsync();

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
                (await session2.Query<Target>().Take(1).ToListAsync()).Single().ShouldBeOfType<Target>();


                var user = new User {FirstName = "Jeremy", LastName = "Miller", UserName = "jeremydmiller"};


                var target = Target.Random();
                session2.Store(target);
                session2.Store(user);

                session2.Events.StartStream(Guid.NewGuid(), new TripStarted {Day = 5},
                    Travel.Random(5), Travel.Random(6)
                );

                await session2.SaveChangesAsync();



                // Just a smoke test
                await session2.QueryAsync(new FindUserByAllTheThings
                {
                    FirstName = "Jeremy", LastName = "Miller", Username = "jeremydmiller"
                });
            }

            Console.WriteLine("IdentityMap");
            using (var session3 = store.OpenSession())
            {
                (await session3.Query<Target>().Take(1).ToListAsync()).Single().ShouldBeOfType<Target>();

                var target = Target.Random();
                session3.Store(target);
                await session3.SaveChangesAsync();

                // Just a smoke test
                await session3.QueryAsync(new FindUserByAllTheThings
                {
                    FirstName = "Jeremy", LastName = "Miller", Username = "jeremydmiller"
                });
            }

            Console.WriteLine("DirtyChecking");
            using (var session4 = store.OpenSession())
            {
                (await session4.Query<Target>().Take(1).ToListAsync()).Single().ShouldBeOfType<Target>();


                var target = Target.Random();
                session4.Store(target);
                await session4.SaveChangesAsync();

                // Just a smoke test
                await session4.QueryAsync(new FindUserByAllTheThings
                {
                    FirstName = "Jeremy", LastName = "Miller", Username = "jeremydmiller"
                });
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
