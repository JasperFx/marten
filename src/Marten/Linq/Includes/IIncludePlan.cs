#nullable enable
using System;
using System.Linq.Expressions;
using Marten.Internal;
using Marten.Linq.SqlGeneration.Filters;

namespace Marten.Linq.Includes;

public interface IIncludePlan
{
    Type DocumentType { get; }
    Expression Where { get; set; }
    IIncludeReader BuildReader(IStorageSession session);

    void AppendStatement(TemporaryTableStatement tempTable, IStorageSession martenSession,
        ITenantFilter tenantFilter);
}
