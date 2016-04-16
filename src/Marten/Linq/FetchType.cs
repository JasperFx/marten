namespace Marten.Linq
{
    /// <summary>
    /// In basic terms, how is the IQueryable going to be executed?
    /// </summary>
    public enum FetchType
    {
        /// <summary>
        /// First/FirstOrDefault/Single/SingleOrDefault
        /// </summary>
        FetchOne,

        /// <summary>
        /// Any execution that returns an IEnumerable (ToArray()/ToList()/etc.)
        /// </summary>
        FetchMany,

        /// <summary>
        /// Using IQueryable.Count()
        /// </summary>
        Count,

        /// <summary>
        /// Using IQueryable.Any()
        /// </summary>
        Any
    }
}