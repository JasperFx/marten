using System;
using System.Threading.Tasks;
using Marten.Metadata;
using Marten.Schema;

namespace Marten.Testing.Examples;


public static class NumericRevisioningSample
{
    public static async Task configure_for_revisioned()
    {
        #region sample_UseNumericRevisions_fluent_interface

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            // Enable numeric document revisioning through the
            // fluent interface
            opts.Schema.For<Incident>().UseNumericRevisions(true);
        });

        #endregion
    }

    #region sample_using_numeric_revisioning

    public static async Task try_revisioning(IDocumentSession session, Reservation reservation)
    {
        // This will create a new document with Version = 1
        session.Insert(reservation);

        // "Store" is an upsert, but if the revisioned document
        // is all new, the Version = 1 after changes are committed
        session.Store(reservation);

        // If Store() is called on an existing document
        // this will just assign the next revision
        session.Store(reservation);

        // *This* operation will enforce the optimistic concurrency
        // The supplied revision number should be the *new* revision number,
        // but will be rejected with a ConcurrencyException when SaveChanges() is
        // called if the version
        // in the database is equal or greater than the supplied revision
        session.UpdateRevision(reservation, 3);

        // This operation will update the document if the supplied revision
        // number is greater than the known database version when
        // SaveChanges() is called, but will do nothing if the known database
        // version is equal to or greater than the supplied revision
        session.TryUpdateRevision(reservation, 3);

        // Any checks happen only here
        await session.SaveChangesAsync();
    }

    #endregion
}

public class Incident
{
    public Guid Id { get; set; }
}


#region sample_versioned_order

public class Order
{
    public Guid Id { get; set; }

    // Marking an integer as the "version"
    // of the document, and making Marten
    // opt this document into the numeric revisioning
    [Version]
    public int Version { get; set; }
}

#endregion

#region sample_versioned_reservation

public class Reservation: IVersioned
{
    public Guid Id { get; set; }

    public Guid Version { get; set; }
}

#endregion
