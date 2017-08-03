using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Util;
using Remotion.Linq.Clauses;

namespace Marten.Linq
{
    public class CompoundWhereFragment : IWhereFragment
    {
        private readonly IList<IWhereFragment> _children = new List<IWhereFragment>();
        private readonly string _separator;

        public CompoundWhereFragment(MartenExpressionParser parser, IQueryableDocument mapping, string separator,
            IEnumerable<WhereClause> wheres)
        {
            _separator = separator;
            _children = wheres.Select(x => parser.ParseWhereFragment(mapping, x.Predicate)).ToArray();
        }

        public CompoundWhereFragment(string separator, params IWhereFragment[] children)
        {
            _separator = separator;
            _children.AddRange(children);
        }

        public void Apply(CommandBuilder builder)
        {
            if (!_children.Any()) return;

            var separator = $" {_separator} ";

            builder.Append("(");
            _children[0].Apply(builder);
            for (var i = 1; i < _children.Count; i++)
            {
                builder.Append(separator);
                _children[i].Apply(builder);
            }

            builder.Append(")");
        }

        public bool Contains(string sqlText)
        {
            return _children.Any(x => x.Contains(sqlText));
        }

        public void Add(IWhereFragment child)
        {
            _children.Add(child);
        }

        public IEnumerable<IWhereFragment> Children => _children;
    }
}