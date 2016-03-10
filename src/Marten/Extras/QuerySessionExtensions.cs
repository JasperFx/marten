using System;
using System.Linq;
using Marten.Util;

namespace Marten.Extras
{
    public static class QuerySessionExtensions
    {
        /// <summary>
        /// Get a document and project it another type, fetching only the fields used in the projection.
        /// </summary>        
        public static TProjection Project<TSource, TProjection>(this IQuerySession session, object id, Projection<TSource, TProjection> projection) where TSource : class where TProjection : class
        {
            if (projection == null) throw new ArgumentNullException(nameof(projection));

            var mapping = session.Store.Schema.MappingFor(typeof(TSource));

            var jsonSelectors = string.Join(",", projection.PropertiesToInclude.Select(x => $"'{x}',{mapping.TableName}.data->'{x}'"));

            var projectionQuery = session.Connection.CreateCommand($"select json_build_object({ jsonSelectors }) from { mapping.TableName } where id = :id").With("id", id);

            using (var reader = projectionQuery.ExecuteReader())
            {
                var found = reader.Read();

                return found ? session.Store.Advanced.Options.Serializer().FromJson<TProjection>(reader.GetString(0)) : null;
            }

        }

        /// <summary>
        /// Get a document and project it another type, fetching only the fields used in the projection.
        /// </summary>        
        public static TProjection Project<TSource, TProjection>(this IDocumentStore store, object id, Projection<TSource, TProjection> projection) where TSource : class where TProjection : class
        {
            if (projection == null) throw new ArgumentNullException(nameof(projection));

            using (var session = store.QuerySession())
            {
                return session.Project(id, projection);
            }
        }
    }
}