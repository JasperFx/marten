using System;
using System.Linq;
using Npgsql;
using System.Collections.Generic;
using Baseline;
using Marten.Schema;
using Marten.Util;
using Remotion.Linq.Clauses;

namespace Marten.Linq
{
    public class CompoundWhereFragment : IWhereFragment
    {
        private readonly string _separator;
        private readonly IList<IWhereFragment> _children = new List<IWhereFragment>();

        public CompoundWhereFragment(MartenExpressionParser parser, IQueryableDocument mapping, string separator, IEnumerable<WhereClause> wheres)
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

        public string ToSql(CommandBuilder command)
        {
            return _children.Select(x => $"({x.ToSql(command)})").Join(" " + _separator + " ");
        }

        public bool Contains(string sqlText)
        {
            return _children.Any(x => x.Contains(sqlText));
        }
    }
}