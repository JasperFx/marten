using System;
using System.Linq;
using Npgsql;
using System.Collections.Generic;
using Baseline;
using Marten.Schema;
using Remotion.Linq.Clauses;

namespace Marten.Linq
{
    public class CompoundWhereFragment : IWhereFragment
    {
        private readonly string _separator;
        private readonly IList<IWhereFragment> _children = new List<IWhereFragment>();

        public CompoundWhereFragment(MartenExpressionParser parser, IDocumentMapping mapping, string separator, IEnumerable<WhereClause> wheres)
        {
            _separator = separator;
            _children = wheres.Select(x => parser.ParseWhereFragment(mapping, x.Predicate)).ToArray();
        }

        public CompoundWhereFragment(string separator, params IWhereFragment[] children)
        {
            _separator = separator;
            _children.AddRange(children);
        }

        public void Add(IWhereFragment child)
        {
            _children.Add(child);
        }

        public string ToSql(NpgsqlCommand command)
        {
            return _children.Select(x => $"({x.ToSql(command)})").Join(" " + _separator + " ");
        }
    }
}