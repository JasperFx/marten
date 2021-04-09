using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
#nullable enable
namespace Marten.Services.BatchQuerying
{
    public interface IBatchedFetcher<T>
    {
        /// <summary>
        /// Return a count of all the documents of type "T"
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<long> Count();

        /// <summary>
        /// Return a count of all the documents of type "T" that match the query
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<long> Count(Expression<Func<T, bool>> filter);

        /// <summary>
        /// Where for the existence of any documents of type "T" matching the query
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<bool> Any();

        /// <summary>
        /// Where for the existence of any documents of type "T"
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<bool> Any(Expression<Func<T, bool>> filter);

        Task<IReadOnlyList<T>> ToList();

        Task<T> First();

        /// <summary>
        /// Find the first document of type "T" matching this query. Will throw an exception if there are no matching documents
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<T> First(Expression<Func<T, bool>> filter);

        /// <summary>
        /// Find the first document of type "T" that matches the query. Will return null if no documents match.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<T?> FirstOrDefault();

        Task<T?> FirstOrDefault(Expression<Func<T, bool>> filter);

        /// <summary>
        /// Returns the single document of type "T" matching this query. Will
        /// throw an exception if the results are null or contain more than one
        /// document
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<T> Single();

        /// <summary>
        /// Returns the single document of type "T" matching this query. Will
        /// throw an exception if the results are null or contain more than one
        /// document
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<T> Single(Expression<Func<T, bool>> filter);

        /// <summary>
        /// Returns the single document of type "T" matching this query or null. Will
        /// throw an exception if the results contain more than one
        /// document
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<T?> SingleOrDefault();

        /// <summary>
        /// Returns the single document of type "T" matching this query or null. Will
        /// throw an exception if the results contain more than one
        /// document
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<T?> SingleOrDefault(Expression<Func<T, bool>> filter);
    }
}
