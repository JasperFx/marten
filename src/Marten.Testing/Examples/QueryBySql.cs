using System.Threading.Tasks;
using Marten.Testing.Documents;

namespace Marten.Testing.Examples;

public class QueryBySql
{
    public async Task QueryForWholeDocumentByWhereClause(IQuerySession session)
    {
        #region sample_query_for_whole_document_by_where_clause

        var millers = await session
            .QueryAsync<User>("where data ->> 'LastName' = 'Miller'");

        #endregion
    }

    public async Task QueryWithParameters(IQuerySession session)
    {
        #region sample_query_with_sql_and_parameters

        var millers = await session
            .QueryAsync<User>("where data ->> 'LastName' = ?", "Miller");

        // custom placeholder parameter
        var millers2 = await session
            .QueryAsync<User>('$', "where data ->> 'LastName' = $", "Miller");

        #endregion
    }

    public async Task QueryAsynchronously(IQuerySession session)
    {
        #region sample_query_with_sql_async

        var millers = await session
            .QueryAsync<User>("where data ->> 'LastName' = ?", "Miller");

        #endregion
    }

}
