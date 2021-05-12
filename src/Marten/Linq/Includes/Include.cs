using System;
using System.Collections.Generic;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Linq.Selectors;

namespace Marten.Linq.Includes
{
    /// <summary>
    /// Used internally to process Include() operations
    /// in the Linq support
    /// </summary>
    public static class Include
    {
        /// <summary>
        /// Used internally to process Include() operations
        /// in the Linq support
        /// </summary>
        public static IIncludeReader ReaderToAction<T>(IMartenSession session, Action<T> action)
        {
            var storage = session.StorageFor<T>();

            var selector = (ISelector<T>) storage.BuildSelector(session);
            return new IncludeReader<T>(action, selector);
        }

        /// <summary>
        /// Used internally to process Include() operations
        /// in the Linq support
        /// </summary>
        public static IIncludeReader ReaderToList<T>(IMartenSession session, IList<T> list)
        {
            return ReaderToAction<T>(session, list.Add);
        }

        /// <summary>
        /// Used internally to process Include() operations
        /// in the Linq support
        /// </summary>
        public static IIncludeReader ReaderToDictionary<T, TId>(IMartenSession session, IDictionary<TId, T> dictionary)
        {
            var storage = session.StorageFor<T>();
            if (storage is IDocumentStorage<T, TId> s)
            {
                void Callback(T item)
                {
                    var id = s.Identity(item);
                    dictionary[id] = item;
                }

                var selector = (ISelector<T>) storage.BuildSelector(session);
                return new IncludeReader<T>(Callback, selector);
            }
            else
            {
                throw new DocumentIdTypeMismatchException(storage, typeof(TId));
            }
        }
    }
}
