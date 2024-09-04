using System;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Core;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Xunit;

namespace EventSourcingTests.QuickAppend;

public class quick_append_timestamp
{
    //[Fact] //-- this was only getting used for a one off test, but I didn't want to throw it away yet
    public async Task see_the_timestamps()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "timestamp";
            opts.Events.AppendMode = EventAppendMode.Quick;
        });

        await store.Advanced.Clean.DeleteAllEventDataAsync();

        using var session = store.LightweightSession();
        session.Events.StartStream(Guid.NewGuid(), new AEvent());
        await session.SaveChangesAsync();

        await Task.Delay(5.Seconds());
        session.Events.StartStream(Guid.NewGuid(), new AEvent());
        await session.SaveChangesAsync();

        await Task.Delay(5.Seconds());
        session.Events.StartStream(Guid.NewGuid(), new AEvent());
        await session.SaveChangesAsync();

        await Task.Delay(5.Seconds());
        session.Events.StartStream(Guid.NewGuid(), new AEvent());
        await session.SaveChangesAsync();

        await Task.Delay(5.Seconds());
        session.Events.StartStream(Guid.NewGuid(), new AEvent());
        await session.SaveChangesAsync();

        await Task.Delay(5.Seconds());
        session.Events.StartStream(Guid.NewGuid(), new AEvent());
        await session.SaveChangesAsync();

        await Task.Delay(5.Seconds());
        session.Events.StartStream(Guid.NewGuid(), new AEvent());
        await session.SaveChangesAsync();
    }
}
