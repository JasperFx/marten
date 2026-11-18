#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Rewriting;

namespace Marten;

// jasperfx#1215: the stored-event rewrite seam a Wolverine job drives, over the rewriter behind
// AdvancedOperations.RewriteStoredEventsAsync. The job builds the run from the event-range marks; the rewriter
// writes each page's cursor into them in the page's own transaction.
public partial class DocumentStore
{
    bool IEventStore.SupportsEventRewrites => true;

    Task<IReadOnlyList<EventRewriteProgress>> IEventStore.RewriteStoredEventsAsync(EventRewriteRun run,
        CancellationToken token)
        => StoredEventRewriter.RunAsync(this, run, token);
}
