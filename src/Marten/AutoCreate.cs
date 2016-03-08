namespace Marten
{
    public enum AutoCreate
    {
        /// <summary>
        /// Will drop and recreate tables that do not match the Marten configuration or create new ones
        /// </summary>
        All,
        
        /// <summary>
        /// Will never destroy existing tables. Attempts to add missing columns or missing tables
        /// </summary>
        CreateOrUpdate,

        /// <summary>
        /// Will create missing schema objects at runtime, but will not update or remove existing schema objects
        /// </summary>
        CreateOnly,
        
        /// <summary>
        /// Do not recreate, destroy, or update schema objects at runtime. Will throw exceptions if
        /// the schema does not match the Marten configuration
        /// </summary>
        None
    }
}