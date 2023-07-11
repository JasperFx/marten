using System;
using Marten.Internal;

namespace Marten.Linq.Includes;

internal interface IIncludePlan
{
    Type DocumentType { get; }
    IIncludeReader BuildReader(IMartenSession session);

    void AppendStatement(TemporaryTableStatement tempTable, IMartenSession martenSession);
}
