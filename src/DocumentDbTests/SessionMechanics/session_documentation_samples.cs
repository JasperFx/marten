using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Documents;

namespace DocumentDbTests.SessionMechanics.SessionSamples;

// Compile-only samples for docs/documents/sessions.md. Nothing here runs as a test; the shared
// session contracts are covered by EventSourcingTests/Compliance/marten_document_storage_compliance.cs.

public class Order
{
    public Guid Id { get; set; }
}

public record OrderPlaced(string CustomerName, decimal Amount);

public class session_documentation_samples
{
    #region sample_shared_contract_session_events
    public async Task append_through_the_shared_contract(
        IDocumentSessionFactory factory, Guid streamKey, CancellationToken token)
    {
        // The session was opened through the shared IDocumentSessionFactory, so nothing here
        // knows it is talking to Marten.
        await using var session = factory.LightweightSession();

        session.Events.StartStream<Order>(streamKey, new OrderPlaced("Acme", 100m));
        await session.SaveChangesAsync(token);
    }
    #endregion

    #region sample_shared_contract_pending_streams
    public async Task read_the_pending_streams(
        IDocumentSessionFactory factory, Guid streamKey, CancellationToken token)
    {
        await using var session = factory.LightweightSession();

        session.Events.StartStream<Order>(streamKey, new OrderPlaced("Acme", 100m));

        // One StreamAction, carrying the OrderPlaced event, before anything is written
        foreach (var stream in session.PendingStreams)
        {
            // stream.ActionType is Start here. It would be Append against an existing stream
            Console.WriteLine($"{stream.ActionType} {stream.Id} with {stream.Events.Count} event(s)");
        }

        // PendingStreams is empty again afterwards
        await session.SaveChangesAsync(token);
    }
    #endregion
}
