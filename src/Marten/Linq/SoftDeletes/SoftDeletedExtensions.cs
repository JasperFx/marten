using System;

namespace Marten.Linq.SoftDeletes;

public static class SoftDeletedExtensions
{
    /// <summary>
    ///     The search results should include all documents, whether
    ///     soft-deleted or not
    /// </summary>
    /// <returns></returns>
    public static bool MaybeDeleted<T>(this T doc)
    {
        return true;
    }

    /// <summary>
    ///     The search results should only include soft-deleted
    ///     documents
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="doc"></param>
    /// <returns></returns>
    public static bool IsDeleted<T>(this T doc)
    {
        return true;
    }

    /// <summary>
    ///     The search results should include documents deleted since given time (&gt;)
    /// </summary>
    /// <param name="doc"></param>
    /// <param name="time"></param>
    /// <returns></returns>
    public static bool DeletedSince<T>(this T doc, DateTimeOffset time)
    {
        return true;
    }

    /// <summary>
    ///     The search results should include documents deleted before given time (&lt;)
    /// </summary>
    /// <param name="doc"></param>
    /// <param name="time"></param>
    /// <returns></returns>
    public static bool DeletedBefore<T>(this T doc, DateTimeOffset time)
    {
        return true;
    }
}
