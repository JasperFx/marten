using System.Collections.Generic;
using System.Linq;
using Marten.Exceptions;

namespace Marten.Events.Projections.Flattened;

public partial class FlatTableProjection
{
    private const string SingleColumnPkRequired = "Flat table projections require a single column primary key";

    private const string EmptyProjection =
        "Empty flat table projections. Register event handlers by calling the Project<T>() or Delete<T>() methods";

    internal override IEnumerable<string> ValidateConfiguration(StoreOptions options)
    {
        foreach (var p in quickValidations()) yield return p;
    }

    private IEnumerable<string> quickValidations()
    {
        if (Table.PrimaryKeyColumns.Count != 1)
        {
            yield return SingleColumnPkRequired;
        }

        if (!_handlers.Any())
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
