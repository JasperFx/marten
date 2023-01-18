using System;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.CodeGeneration;
using Marten;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Oakton;
using Shouldly;
using Spectre.Console;

namespace CommandLineRunner;

public class TestCommand : OaktonAsyncCommand<NetCoreInput>
{
    public override async Task<bool> Execute(NetCoreInput input)
    {
        using var host = input.BuildHost();

        var collections = host.Services.GetServices<ICodeFileCollection>().ToArray();
        foreach (var collection in collections)
        {
            Console.WriteLine(collection);
            Console.WriteLine("  " + collection.Rules.GeneratedCodeOutputPath);

            var files = collection.BuildFiles();
            if (files.Any())
            {
                foreach (var file in files)
                {
                    Console.WriteLine("    * " + file);
                }
            }
            else
            {
                Console.WriteLine("    * NONE");
            }
        }

        using var store = host.Services.GetRequiredService<IDocumentStore>();
        await store.Advanced.Clean.DeleteAllDocumentsAsync();

        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var targets = Target.GenerateRandomData(1000).ToArray();

        // Bulk Insert
        Console.WriteLine("Bulk Writing");
        await store.BulkInsertDocumentsAsync(targets);

        Console.WriteLine("QueryOnly");
        await using (var session1 = await store.QuerySessionAsync())
        {
            (await session1.Query<Target>().Take(1).ToListAsync()).Single().ShouldBeOfType<Target>();
        }

        Console.WriteLine("Lightweight");
        await using (var session2 = await store.LightweightSessionAsync())
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
        await using (var session3 = await store.IdentitySessionAsync())
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
        await using (var session4 = await store.DirtyTrackedSessionAsync())
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
        await using (var session = await store.LightweightSessionAsync())
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

        AnsiConsole.Write("[green]All Good![/]");

        return true;
    }
}
