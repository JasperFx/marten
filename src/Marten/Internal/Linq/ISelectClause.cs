using System;
using Marten.Linq;
using Marten.Util;

namespace Marten.Internal.Linq
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
