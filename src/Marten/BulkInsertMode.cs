namespace Marten
{
    public enum BulkInsertMode
    {
        /// <summary>
        /// Default, fast mode. Will throw an exception if there are any duplicate id's with the existing data
        /// </summary>
        InsertsOnly,

        /// <summary>
        /// Will ignore any documents that already exist in the underlying table storage
        /// </summary>
        IgnoreDuplicates,

        /// <summary>
        /// Will overwrite the values of any duplicate documents (last update wins)
        /// </summary>
        OverwriteExisting
    }
}
