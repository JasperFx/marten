#nullable enable
using System;
using System.Collections.Generic;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Linq.Selectors;

namespace Marten.Linq.Includes;

/// <summary>
///     Used internally to process Include() operations
///     in the Linq support
/// </summary>
public static class Include
{
    /// <summary>
    ///     Used internally to process Include() operations
    ///     in the Linq support
    /// </summary>
    public static IIncludeReader ReaderToAction<T>(IStorageSession session, Action<T> action) where T : notnull
    {
        var storage = session.StorageFor<T>();

        var selector = (ISelector<T>)storage.BuildSelector(session);
        return new IncludeReader<T>(action, selector);
    }

    /// <summary>
    ///     Used internally to process Include() operations
    ///     in the Linq support
    /// </summary>
    public static IIncludeReader ReaderToList<T>(IStorageSession session, IList<T> list) where T : notnull
    {
        return ReaderToAction<T>(session, list.Add);
    }

    /// <summary>
    ///     Used internally to process Include() operations
    ///     in the Linq support
    /// </summary>
    public static IIncludeReader ReaderToDictionary<T, TId>(IStorageSession session, IDictionary<TId, T> dictionary) where T : notnull where TId : notnull
    {
        var storage = session.StorageFor<T>();
        if (storage is IDocumentStorage<T, TId> s)
        {
            void Callback(T item)
            {
                var id = s.Identity(item);
                dictionary[id] = item;
            }

            var selector = (ISelector<T>)storage.BuildSelector(session);
            return new IncludeReader<T>(Callback, selector);
        }

        throw new DocumentIdTypeMismatchException(storage, typeof(TId));
    }
}
