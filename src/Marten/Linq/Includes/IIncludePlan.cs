using System;
using Marten.Internal;
using Marten.Linq.SqlGeneration.Filters;

namespace Marten.Linq.Includes;

public interface IIncludePlan
{
    Type DocumentType { get; }
    IIncludeReader BuildReader(IMartenSession session);

    void AppendStatement(TemporaryTableStatement tempTable, IMartenSession martenSession,
        ITenantFilter tenantFilter);
}
