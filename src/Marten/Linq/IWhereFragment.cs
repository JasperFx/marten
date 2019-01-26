using System.Linq;
using Baseline;
using Marten.Linq.Parsing;
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

        public static IWhereFragment Append(this IWhereFragment fragment, IWhereFragment[] others)
        {
            if (others?.Any() == false) return fragment;

            if (fragment == null) return Append(others.First(), others.Skip(1).ToArray());

            foreach (var other in others)
            {
                fragment = fragment.Append(other);
            }

            return fragment;
        }

        public static IWhereFragment[] Flatten(this IWhereFragment fragment)
        {
            if (fragment == null) return new IWhereFragment[0];

            if (fragment is CompoundWhereFragment)
            {
                return fragment.As<CompoundWhereFragment>().Children.ToArray();
            }

            return new IWhereFragment[] { fragment };
        }

        public static string ToSql(this IWhereFragment fragment)
        {
            if (fragment == null) return null;

            var cmd = new NpgsqlCommand();
            var builder = new CommandBuilder(cmd);
            fragment.Apply(builder);

            return builder.ToString().Trim();
        }

        public static bool SpecifiesTenant(this IWhereFragment fragment)
        {
            return fragment.Flatten().OfType<ITenantWhereFragment>().Any();
        }
    }
}