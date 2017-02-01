using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq.Model;
using Marten.Services;
using Marten.Util;
using Npgsql;

namespace Marten.Linq.QueryHandlers
{
    public class JsonQueryHandler : IQueryHandler<string>
    {
        private readonly LinqQuery<string> _query;

        public JsonQueryHandler(LinqQuery<string> query)
        {
            _query = query;
        }


        public Type SourceType => _query.SourceType;

        public void ConfigureCommand(CommandBuilder builder)
        {
            _query.ConfigureCommand(builder);
        }

        public string Handle(DbDataReader reader, IIdentityMap map, QueryStatistics stats)
        {
            return $"[{_query.Selector.Read(reader, map, stats).ToArray().Join(",")}]";
        }

        public async Task<string> HandleAsync(DbDataReader reader, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            var result = await _query.Selector.ReadAsync(reader, map, stats, token).ConfigureAwait(false);
            return $"[{result}]";
        }
    }
}