namespace Marten
{
    public enum SecurityRights
    {
        /// <summary>
        /// Upsert functions will execute with the rights of the current Postgresql user. This is the default
        /// in both Marten and Postgresql.
        /// </summary>
        Invoker,

        /// <summary>
        /// Upsert functions will execute with the rights of the Postgresql user that created the schema
        /// objects.
        /// </summary>
        Definer
    }
}
