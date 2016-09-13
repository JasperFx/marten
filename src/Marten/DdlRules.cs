using System.Collections.Generic;

namespace Marten
{
    public class DdlRules
    {
        public static readonly string SCHEMA = "%SCHEMA%";
        public static readonly string TABLENAME = "%TABLENAME%";
        public static readonly string FUNCTION = "%FUNCTION%";
        public static readonly string SIGNATURE = "%SIGNATURE%";
        public static readonly string COLUMNS = "%COLUMNS%";
        public static readonly string NON_ID_COLUMNS = "%NON_ID_COLUMNS%";
        public static readonly string METADATA_COLUMNS = "%METADATA_COLUMNS%";


        /// <summary>
        /// Alters the syntax used to create tables in DDL
        /// </summary>
        public CreationStyle TableCreation { get; set; } = CreationStyle.DropThenCreate;

        /// <summary>
        /// Alters the user rights for the upsert functions in DDL
        /// </summary>
        public SecurityRights UpsertRights { get; set; } = SecurityRights.Invoker;

        /// <summary>
        /// Option to use this database role during DDL scripts
        /// </summary>
        public string Role { get; set; }
    }
}