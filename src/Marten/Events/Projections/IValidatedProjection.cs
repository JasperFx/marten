using System.Collections.Generic;

namespace Marten.Events.Projections
{
    internal interface IValidatedProjection
    {
        void AssertValidity();

        IEnumerable<string> ValidateConfiguration(StoreOptions options);
    }
}
