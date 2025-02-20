#nullable enable
using System.Collections.Generic;

namespace Marten.Events.Projections;

internal interface IValidatedProjection
{
    IEnumerable<string> ValidateConfiguration(StoreOptions options);
}
