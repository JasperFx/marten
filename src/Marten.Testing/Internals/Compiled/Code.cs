using System.Collections.Generic;
using Marten.Internal;
using Marten.Internal.CompiledQueries;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Testing.Documents;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Testing.Internals.Compiled
{
    // START: OpenIssuesAssignedToUserCompiledQuery
    public class
        OpenIssuesAssignedToUserCompiledQuery: ClonedCompiledQuery<IEnumerable<Issue>, OpenIssuesAssignedToUser>
    {
        private readonly HardCodedParameters _hardcoded;
        private readonly IMaybeStatefulHandler _inner;
        private readonly OpenIssuesAssignedToUser _query;
        private readonly QueryStatistics _statistics;

        public OpenIssuesAssignedToUserCompiledQuery(IMaybeStatefulHandler inner, OpenIssuesAssignedToUser query,
            QueryStatistics statistics, HardCodedParameters hardcoded): base(inner, query, statistics, hardcoded)
        {
            _inner = inner;
            _query = query;
            _statistics = statistics;
            _hardcoded = hardcoded;
        }


        public override void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            var parameters = builder.AppendWithParameters(
                @"select d.id, d.data from public.mt_doc_issue as d where (CAST(d.data ->> 'AssigneeId' as uuid) = ? and  d.data ->> 'Status' = ?)");

            parameters[0].NpgsqlDbType = NpgsqlDbType.Uuid;
            parameters[0].Value = _query.UserId;
            _hardcoded.Apply(parameters);
        }
    }

    // END: OpenIssuesAssignedToUserCompiledQuery


    // START: OpenIssuesAssignedToUserCompiledQuerySource
    public class
        OpenIssuesAssignedToUserCompiledQuerySource: CompiledQuerySource<IEnumerable<Issue>, OpenIssuesAssignedToUser>
    {
        private readonly HardCodedParameters _hardcoded;
        private readonly IMaybeStatefulHandler _maybeStatefulHandler;

        public OpenIssuesAssignedToUserCompiledQuerySource(HardCodedParameters hardcoded,
            IMaybeStatefulHandler maybeStatefulHandler)
        {
            _hardcoded = hardcoded;
            _maybeStatefulHandler = maybeStatefulHandler;
        }


        public override IQueryHandler<IEnumerable<Issue>> BuildHandler(OpenIssuesAssignedToUser query,
            IMartenSession session)
        {
            return new OpenIssuesAssignedToUserCompiledQuery(_maybeStatefulHandler, query, null, _hardcoded);
        }
    }

    // END: OpenIssuesAssignedToUserCompiledQuerySource
}
