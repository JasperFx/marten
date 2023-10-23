#nullable enable
using System;

namespace Marten.Linq.CreatedAt;

public static class CreatedAtExtensions
{
    /// <summary>
    ///     The search results should include documents created since given time (&gt;)
    /// </summary>
    /// <param name="doc"></param>
    /// <param name="time"></param>
    /// <returns></returns>
    public static bool CreatedSince(this object doc, DateTimeOffset time)
    {
        return true;
    }

    /// <summary>
    ///     The search results should include documents created before given time (&lt;)
    /// </summary>
    /// <param name="doc"></param>
    /// <param name="time"></param>
    /// <returns></returns>
    public static bool CreatedBefore(this object doc, DateTimeOffset time)
    {
        return true;
    }
}
