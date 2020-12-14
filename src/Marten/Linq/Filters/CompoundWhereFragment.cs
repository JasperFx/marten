using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Linq.SqlGeneration;
using Marten.Util;

namespace Marten.Linq.Filters
{
    public class CompoundWhereFragment: ISqlFragment, IWhereFragmentHolder
    {
        private readonly IList<ISqlFragment> _children = new List<ISqlFragment>();
        private readonly string _separator;

        public CompoundWhereFragment(string separator, params ISqlFragment[] children)
        {
            _separator = separator;
            _children.AddRange(children);
        }

        public void Register(ISqlFragment fragment)
        {
            _children.Add(fragment);
        }

        public void Apply(CommandBuilder builder)
        {
            if (!_children.Any())
                return;

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

        public void Add(ISqlFragment child)
        {
            _children.Add(child);
        }

        public void Remove(ISqlFragment fragment)
        {
            _children.Remove(fragment);
        }

        public IEnumerable<ISqlFragment> Children => _children;
    }
}
