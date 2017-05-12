using System.Linq;
using Baseline;
using Marten.Util;
using Npgsql;

namespace Marten.Linq
{
    public interface IWhereFragment
    {
        void Apply(CommandBuilder builder);
        bool Contains(string sqlText);
    }

    public static class WhereFragmentExtensions
    {
        public static IWhereFragment Append(this IWhereFragment fragment, IWhereFragment other)
        {
            if (fragment is CompoundWhereFragment)
            {
                fragment.As<CompoundWhereFragment>().Add(other);
                return fragment;
            }
            else if (other is CompoundWhereFragment)
            {
                other.As<CompoundWhereFragment>().Add(fragment);
                return other;
            }

            return new CompoundWhereFragment("and", fragment, other);

        }

        public static IWhereFragment[] Flatten(this IWhereFragment fragment)
        {
            if (fragment == null) return new IWhereFragment[0];

            if (fragment is CompoundWhereFragment)
            {
                return fragment.As<CompoundWhereFragment>().Children.ToArray();
            }

            return new IWhereFragment[] {fragment};
        }

        public static string ToSql(this IWhereFragment fragment)
        {
            if (fragment == null) return null;

            var cmd = new NpgsqlCommand();
            var builder = new CommandBuilder(cmd);
            fragment.Apply(builder);

            return builder.ToString().Trim();
        }

    }
}