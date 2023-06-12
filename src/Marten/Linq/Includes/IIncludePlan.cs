using System;
using Marten.Internal;
using Marten.Linq.SqlGeneration;

namespace Marten.Linq.Includes;

internal interface IIncludePlan
{
    string IdAlias { get; }
    string TempTableSelector { get; }
    int Index { set; }
    string ExpressionName { get; }

    Type DocumentType { get; }
    IIncludeReader BuildReader(IMartenSession session);
    bool IsIdCollection();
    Statement BuildStatement(string tempTableName, IPagedStatement paging, IMartenSession session);
}
