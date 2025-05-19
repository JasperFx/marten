using System.Collections.Generic;
using System.Linq;
using JasperFx.Events.Projections;
using JasperFx.Events.Subscriptions;

namespace Marten.Events.Projections.Flattened;

public partial class FlatTableProjection: ISubscriptionFactory<IDocumentOperations, IQuerySession>
{
    private const string SingleColumnPkRequired = "Flat table projections require a single column primary key";

    private const string EmptyProjection =
        "Empty flat table projections. Register event handlers by calling the Project<T>() or Delete<T>() methods";

    private IEnumerable<string> quickValidations()
    {
        if (Table.PrimaryKeyColumns.Count != 1)
        {
            yield return SingleColumnPkRequired;
        }

        if (_handlers.IsEmpty)
        {
            yield return EmptyProjection;
        }
    }

    public override void AssembleAndAssertValidity()
    {
        var messages = quickValidations().ToArray();
        if (messages.Any())
        {
            throw new InvalidProjectionException(messages);
        }
    }
}
