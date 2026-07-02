using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

// Repro: within a SINGLE unit of work, does the global seq_id reflect the order
// events were appended in code, or the first-touched-stream grouping?
//
// Code append order across the unit of work is: B1, A1, B2.
// Intuitive expectation: Sequence(A1) < Sequence(B2) because A1 was appended
// before B2. QuickEventAppender assigns seq_ids by iterating
// session.WorkTracker.Streams in first-touch order (B touched first), so both
// B events get the earliest seq_ids and A1 ends up HIGHER than B2.
public abstract class seq_id_reflects_append_order_within_unit_of_work : OneOffConfigurationsContext
{
    public record EventA(string Note);

    public record EventB(string Note);

    [Fact]
    public async Task seq_id_should_follow_append_order()
    {
        const string streamA = "A";
        const string streamB = "B";

        // Code append order: B1, A1, B2
        theSession.Events.StartStream(streamB, new EventB("B1")); // B touched first
        theSession.Events.StartStream(streamA, new EventA("A1")); // A touched second
        theSession.Events.Append(streamB, new EventB("B2"));      // back to B, appended last

        await theSession.SaveChangesAsync();

        var a1 = (await theSession.Events.FetchStreamAsync(streamA)).Single();
        var bEvents = await theSession.Events.FetchStreamAsync(streamB);
        var b1 = bEvents.Single(e => e.Version == 1);
        var b2 = bEvents.Single(e => e.Version == 2);

        // Print the actual sequences so the inversion is visible in test output.
        System.Console.WriteLine(
            $"seq_ids: B1={b1.Sequence}, A1={a1.Sequence}, B2={b2.Sequence}");

        // A1 was appended BEFORE B2 in code -> intuitively should have a lower seq_id.
        a1.Sequence.ShouldBeLessThan(b2.Sequence);
    }

    public class quick_with_server_timestamps : seq_id_reflects_append_order_within_unit_of_work
    {
        public quick_with_server_timestamps()
        {
            StoreOptions(opts =>
            {
                opts.Events.StreamIdentity = StreamIdentity.AsString;
                opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            });
        }
    }

    public class rich_default_mode : seq_id_reflects_append_order_within_unit_of_work
    {
        public rich_default_mode()
        {
            StoreOptions(opts =>
            {
                opts.Events.StreamIdentity = StreamIdentity.AsString;
                // Default mode is Rich; set explicitly for clarity.
                opts.Events.AppendMode = EventAppendMode.Rich;
            });
        }
    }
}
