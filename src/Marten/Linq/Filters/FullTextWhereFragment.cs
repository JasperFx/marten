using System.Linq;
using Marten.Linq.Parsing;
using Marten.Linq.Parsing.Methods;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;
using Marten.Schema;
using Marten.Util;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Filters
{
    internal class FullTextWhereFragment: ISqlFragment
    {
        private readonly string _regConfig;
        private readonly string _dataConfig;
        private readonly FullTextSearchFunction _searchFunction;
        private readonly string _searchTerm;

        private string Sql => $"to_tsvector('{_regConfig}'::regconfig, {_dataConfig}) @@ {_searchFunction}('{_regConfig}'::regconfig, '{SanitizeSearchTerm(_searchTerm)}')";

        public FullTextWhereFragment(DocumentMapping mapping, FullTextSearchFunction searchFunction, string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig)
        {
            _regConfig = regConfig;

            _dataConfig = GetDataConfig(mapping, regConfig).Replace("data", "d.data");
            _searchFunction = searchFunction;
            _searchTerm = searchTerm;
        }

        private string SanitizeSearchTerm(string searchTerm)
        {
            // edge case that will cause a sql exception with a single quote we need to handle also
            return searchTerm == "'" ? "" : searchTerm?.Replace("'", "''");
        }

        public void Apply(CommandBuilder builder)
        {
            // don't use parameters for to_tsvector as it ruins the performance with the query plan in PG
            builder.Append(Sql);
        }

        public bool Contains(string sqlText)
        {
            return Sql.Contains(sqlText);
        }

        private static string GetDataConfig(DocumentMapping mapping, string regConfig)
        {
            if (mapping == null)
                return FullTextIndex.DefaultDataConfig;

            return mapping
                .Indexes
                .OfType<FullTextIndex>()
                .Where(i => i.RegConfig == regConfig)
                .Select(i => i.DataConfig)
                .FirstOrDefault() ?? FullTextIndex.DefaultDataConfig;
        }
    }
}
