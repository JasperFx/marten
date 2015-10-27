using System;
using System.Linq;
using Npgsql;
using System.Collections.Generic;
using Remotion.Linq.Clauses;

namespace Marten.Linq
{
    public class CompoundWhereFragment : IWhereFragment
    {
        private readonly string _separator;
        private readonly IWhereFragment[] _children;

        public CompoundWhereFragment(Type rootType, string separator, IEnumerable<WhereClause> wheres)
        {
            _separator = separator;
            _children = wheres.Select(x => MartenExpressionParser.ParseWhereFragment(rootType, x.Predicate)).ToArray();
        }

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