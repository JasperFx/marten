using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;

namespace EventSourcingTests.Examples;

public record ReservationCancelled;

public class Reservation
{
    public Guid Id { get; set; }
    public bool Cancelled { get; set; }

    public void Apply(ReservationCancelled _) => Cancelled = true;
}

public static class BatchReads
{
    #region sample_fetch_many_for_writing

    public static async Task CancelAll(IDocumentSession session, IReadOnlyList<Guid> reservationIds)
    {
        // One round trip for every stream, rather than one round trip each.
        var streams = await session.Events
            .FetchManyForWriting<Reservation>(reservationIds);

        // The handles come back in the order you asked for them, so you can
        // zip the result against your own ids.
        foreach (var stream in streams)
        {
            // A stream that does not exist yet is still a handle -- its
            // Aggregate is null and its version is 0.
            if (stream.Aggregate is { Cancelled: false })
            {
                stream.AppendOne(new ReservationCancelled());
            }
        }

        // Each handle kept its own starting version, so this still guards
        // every stream it appended to -- and no others.
        await session.SaveChangesAsync();
    }

    #endregion

    #region sample_load_many_through_the_shared_contract

    public static async Task<IReadOnlyList<Reservation>> LoadAll(
        JasperFx.Events.Documents.IDocumentReadOperations session,
        IEnumerable<Guid> ids,
        CancellationToken token)
    {
        // Store-agnostic, and on Marten this is a single round trip.
        // Ids with no document are left out, and a repeated id comes back once.
        return await session.LoadManyAsync<Reservation>(ids, token);
    }

    #endregion
}
