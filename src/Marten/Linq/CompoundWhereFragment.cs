using System.Linq;
using Npgsql;
using System.Collections.Generic;

namespace Marten.Linq
{
    public class CompoundWhereFragment : IWhereFragment
    {
        private readonly string _separator;
        private readonly IWhereFragment[] _children;

        public CompoundWhereFragment(string separator, params IWhereFragment[] children)
        {
            _separator = separator;
            _children = children;
        }

        public string ToSql(NpgsqlCommand command)
        {
            return _children.Select(x => x.ToSql(command)).Join(" " + _separator + " ");
        }
    }
}