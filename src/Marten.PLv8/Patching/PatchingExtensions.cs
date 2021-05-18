using System;
using System.Linq.Expressions;
using Baseline;
using Marten.Internal.Sessions;
using Marten.PLv8.Transforms;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.PLv8.Patching
{
    public static class PatchingExtensions
    {


        private static IPatchExpression<T> patchById<T>(IDocumentOperations operations, object id)
        {
            operations.EnsureTransformsExist();
            var where = new WhereFragment("d.id = ?", id);
            return new PatchExpression<T>(where, (DocumentSessionBase)operations);
        }

        /// <summary>
        /// Patch a single document of type T with the given id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        public static IPatchExpression<T> Patch<T>(this IDocumentOperations operations, int id) where T : notnull
        {
            return patchById<T>(operations, id);
        }

        /// <summary>
        /// Patch a single document of type T with the given id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        public static IPatchExpression<T> Patch<T>(this IDocumentOperations operations, long id) where T : notnull
        {
            return patchById<T>(operations, id);
        }

        /// <summary>
        /// Patch a single document of type T with the given id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        public static IPatchExpression<T> Patch<T>(this IDocumentOperations operations, string id) where T : notnull
        {
            return patchById<T>(operations, id);
        }

        /// <summary>
        /// Patch a single document of type T with the given id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        public static IPatchExpression<T> Patch<T>(this IDocumentOperations operations, Guid id) where T : notnull
        {
            return patchById<T>(operations, id);
        }

        /// <summary>
        /// Patch a single document of type T with the given id
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <param name="where"></param>
        /// <returns></returns>
        public static IPatchExpression<T> Patch<T>(this IDocumentOperations operations, Expression<Func<T, bool>> filter) where T : notnull
        {
            operations.EnsureTransformsExist();
            return new PatchExpression<T>(filter, (DocumentSessionBase) operations);
        }

        /// <summary>
        /// Patch multiple documents matching the supplied where fragment
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="fragment"></param>
        /// <returns></returns>
        public static IPatchExpression<T> Patch<T>(this IDocumentOperations operations, ISqlFragment fragment) where T : notnull
        {
            operations.EnsureTransformsExist();
            return new PatchExpression<T>(fragment, (DocumentSessionBase) operations);
        }
    }
}
