using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using System.Reflection;
using Marten.Util;

namespace Marten.Events.Projections
{

    [Obsolete("Remove in V4")]
    public class ProjectionCollection: IEnumerable<IProjection>
    {
        private readonly StoreOptions _options;
        private readonly IList<IProjection> _projections = new List<IProjection>();

        public ProjectionCollection(StoreOptions options)
        {
            _options = options;
        }

        public IEnumerator<IProjection> GetEnumerator()
        {
            return _projections.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Add(IProjection projection)
        {
            if (projection == null)
                throw new ArgumentNullException(nameof(projection));

            if (projection is IDocumentProjection)
            {
                _options.Storage.MappingFor(projection.ProjectedType());
            }

            _projections.Add(projection);
        }

        public void Add<T>() where T : IProjection
        {
            Add(New<T>.Instance());
        }

        public IProjection ForView(Type viewType)
        {
            return _projections.FirstOrDefault(x => x.ProjectedType() == viewType);
        }
    }
}
