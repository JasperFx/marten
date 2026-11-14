using Marten.Linq.SqlGeneration;

namespace Marten.Internal.Storage;

internal interface IQueryOnlyDocumentStorage: IDocumentStorage
{
    ISelectClause SelectClauseForIncludes();
}
