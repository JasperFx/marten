using System.Linq;
using Marten.Linq.Parsing;
using Marten.Schema;
using Marten.Util;

namespace Marten.Linq.WhereFragments
{
    internal class FullTextWhereFragment : IWhereFragment
    {
        private readonly string _regConfig;
        private readonly string _dataConfig;
        private readonly FullTextSearchFunction _searchFunction;
        private readonly string _searchTerm;

        private string Sql => $"to_tsvector(:argRegConfig::regconfig, {_dataConfig}) @@ {_searchFunction}(:argRegConfig::regconfig, :argSearchTerm)";

        public FullTextWhereFragment(DocumentMapping mapping, FullTextSearchFunction searchFunction, string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig)
        {
            _regConfig = regConfig;
            _dataConfig = GetDataConfig(mapping, regConfig);
            _searchFunction = searchFunction;
            _searchTerm = searchTerm;
        }

        public void Apply(CommandBuilder builder)
        {
            builder.AddNamedParameter("argRegConfig", _regConfig);
            builder.AddNamedParameter("argSearchTerm", _searchTerm);

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