using System;
using System.Linq.Expressions;

namespace Marten.Transforms
{
    // TODO -- needs to be async options for all of these
    public interface IDocumentTransforms
    {
        /// <summary>
        /// Apply the named transform to all documents of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="transformName"></param>
        void All<T>(string transformName);

        /// <summary>
        /// Apply the named transform to documents of type T
        /// matching the supplied "where" clause
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="transformName"></param>
        /// <param name="where"></param>
        void Where<T>(string transformName, Expression<Func<T, bool>> where);

        /// <summary>
        /// Apply the named transform to only the specified tenant
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="transformName"></param>
        /// <param name="tenantId"></param>
        void Tenant<T>(string transformName, string tenantId);

        /// <summary>
        /// Apply the named transform to only the specified tenants
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="transformName"></param>
        /// <param name="tenantIds"></param>
        void Tenants<T>(string transformName, params string[] tenantIds);

        void Document<T>(string transformName, string id);

        void Document<T>(string transformName, int id);

        void Document<T>(string transformName, long id);

        void Document<T>(string transformName, Guid id);
    }
}
