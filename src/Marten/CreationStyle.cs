namespace Marten
{
    public enum CreationStyle
    {
        /// <summary>
        /// Export DDL by first issuing a DROP statement for a table, then the CREATE statement. This is the default
        /// </summary>
        DropThenCreate,

        /// <summary>
        /// Export DDL for table creation by using a CREATE IF NOT EXISTS clause w/o a prior DROP statement
        /// </summary>
        CreateIfNotExists
    }
}