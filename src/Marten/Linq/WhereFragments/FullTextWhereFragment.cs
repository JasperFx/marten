using System.Linq;
using Marten.Schema;

namespace Marten.Linq.WhereFragments
{
    internal class FullTextWhereFragment : WhereFragment
    {
        public FullTextWhereFragment(DocumentMapping mapping, string searchTerm, string regConfig, string searchFunction)
            : base(GetFilter(mapping, searchTerm, regConfig, searchFunction))
        {
        }

        private static string GetFilter(DocumentMapping mapping, string searchTerm, string regConfig, string searchFunction)
        {
            var dataConfig = GetDataConfig(mapping, regConfig);

            return $"to_tsvector('{regConfig}', {dataConfig}) @@ {searchFunction}('{regConfig}', '{searchTerm}')";
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