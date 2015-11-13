namespace Marten.Schema
{
    public enum PostgresUpsertType
    {
        /// <summary>
        /// uses the 9.5 and up "ON CONFLICT" upsert style
        /// </summary>
        Standard,
        /// <summary>
        /// uses the 9.4 upsert functionality ("ON CONFLICT" not available before 9.5)
        /// </summary>
        Legacy
    }
}
