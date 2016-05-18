using System;
using DinnerParty.Helpers;
using Marten;

namespace DinnerParty.Models.Marten
{
    /// <summary>
    /// This class demonstrates using a <seealso cref="DocumentSessionListenerBase"/> to ensure that 
    /// the <see cref="Dinner.LastModified"/> property is always updated before an insert/update opration
    /// </summary>
    /// <remarks>
    /// See: https://github.com/JasperFx/marten/blob/master/documentation/documentation/documents/diagnostics.md#listening-for-document-store-events
    /// 
    /// This is redundant in newer versions (https://github.com/JasperFx/marten/commit/bf1acfa812031a5392988b753ee6f2f18ff62d0e)
    ///  of marten as it has support for a last modified column, but this is left in for demonstration purposes.
    /// </remarks>
    public class LastUpdatedSessionListener : DocumentSessionListenerBase
    {
        public override void BeforeSaveChanges(IDocumentSession session)
        {
            // Get a set of pending changes for this session
            var pending = session.PendingChanges;

            // For each dinner that is to be inserted, set the Dinner.LastModified property to now
            pending.InsertsFor<Dinner>().Each(d => d.LastModified = DateTime.Now);

            // For each dinner that is to be updated, set the Dinner.LastModified property to now
            pending.UpdatesFor<Dinner>().Each(d => d.LastModified = DateTime.Now);
        }
    }
}