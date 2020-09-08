using System;
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Util;

namespace Marten.Linq.SqlGeneration
{
    public interface ISelectClause
    {
        string FromObject { get; }

        Type SelectedType { get; }

        void WriteSelectClause(CommandBuilder sql);

        string[] SelectFields();

        ISelector BuildSelector(IMartenSession session);
        IQueryHandler<T> BuildHandler<T>(IMartenSession session, Statement topStatement, Statement currentStatement);
        ISelectClause UseStatistics(QueryStatistics statistics);
    }
}
