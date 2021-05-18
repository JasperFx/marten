using System;
using System.Linq.Expressions;

namespace Marten.PLv8.Transforms
{
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

        /// <summary>
        /// Transform a single document by its Id using a named Javascript transform
        /// </summary>
        /// <param name="transformName"></param>
        /// <param name="id"></param>
        /// <typeparam name="T"></typeparam>
        void Document<T>(string transformName, string id);

        /// <summary>
        /// Transform a single document by its Id using a named Javascript transform
        /// </summary>
        /// <param name="transformName"></param>
        /// <param name="id"></param>
        /// <typeparam name="T"></typeparam>
        void Document<T>(string transformName, int id);

        /// <summary>
        /// Transform a single document by its Id using a named Javascript transform
        /// </summary>
        /// <param name="transformName"></param>
        /// <param name="id"></param>
        /// <typeparam name="T"></typeparam>
        void Document<T>(string transformName, long id);

        /// <summary>
        /// Transform a single document by its Id using a named Javascript transform
        /// </summary>
        /// <param name="transformName"></param>
        /// <param name="id"></param>
        /// <typeparam name="T"></typeparam>
        void Document<T>(string transformName, Guid id);
    }
}
