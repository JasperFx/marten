using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Marten.Extras
{
    public class ProjectionBuilder<TSource>
    {
        private readonly IEnumerable<string> _propertiesToInclude;

        private ProjectionBuilder()
        {
            _propertiesToInclude = Enumerable.Empty<string>();
        }

        public ProjectionBuilder(IEnumerable<string> propertiesToInclude)
        {
            this._propertiesToInclude = propertiesToInclude;
        }

        public static ProjectionBuilder<TSource> Project()
        {
            return new ProjectionBuilder<TSource>();
        }

        public ProjectionBuilder<TSource> Include(Expression<Func<TSource, object>> field)
        {
            var expression = (MemberExpression)field.Body;
            var name = expression.Member.Name;
            return new ProjectionBuilder<TSource>(_propertiesToInclude.Concat(new[] { name }));
        }

        public Projection<TSource, TProjection> To<TProjection>()
        {
            return new Projection<TSource, TProjection>(_propertiesToInclude);
        }
    }
}