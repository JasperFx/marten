using System.Linq;
using Marten.Linq.Filters;
using Marten.Linq.Parsing;
using Marten.Linq.Parsing.Methods;
using Weasel.Postgresql;
using Marten.Util;
using Npgsql;

namespace Marten.Linq.SqlGeneration
{
    public interface ISqlFragment
    {
        void Apply(CommandBuilder builder);

        bool Contains(string sqlText);
    }

    public static class WhereFragmentExtensions
    {
        public static ISqlFragment[] Flatten(this ISqlFragment fragment)
        {
            if (fragment == null)
                return new ISqlFragment[0];

            if (fragment is CompoundWhereFragment c)
            {
                return c.Children.ToArray();
            }

            return new [] { fragment };
        }

        public static string ToSql(this ISqlFragment fragment)
        {
            if (fragment == null)
                return null;

            var cmd = new NpgsqlCommand();
            var builder = new CommandBuilder(cmd);
            fragment.Apply(builder);

            return builder.ToString().Trim();
        }

        public static bool SpecifiesTenant(this ISqlFragment fragment)
        {
            return fragment.Flatten().OfType<ITenantWhereFragment>().Any();
        }
    }
}
