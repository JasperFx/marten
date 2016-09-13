using System;
using System.IO;
using Baseline;

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

        public readonly LightweightCache<string, DdlTemplate> Templates
            = new LightweightCache<string, DdlTemplate>(name => new DdlTemplate(name));


        /// <summary>
        ///     Alters the syntax used to create tables in DDL
        /// </summary>
        public CreationStyle TableCreation { get; set; } = CreationStyle.DropThenCreate;

        /// <summary>
        ///     Alters the user rights for the upsert functions in DDL
        /// </summary>
        public SecurityRights UpsertRights { get; set; } = SecurityRights.Invoker;

        /// <summary>
        ///     Option to use this database role during DDL scripts
        /// </summary>
        public string Role { get; set; }

        /// <summary>
        /// Read [name].table and [name].function files from the named directory
        /// to serve as templates for extra DDL (GRANT's probably)
        /// </summary>
        /// <param name="directory"></param>
        public void ReadTemplates(string directory)
        {
            var system = new FileSystem();

            system.FindFiles(directory, FileSet.Shallow("*.function")).Each(file =>
            {
                var name = Path.GetFileNameWithoutExtension(file).ToLower();

                Templates[name].FunctionCreation = system.ReadStringFromFile(file);
            });

            system.FindFiles(directory, FileSet.Shallow("*.table")).Each(file =>
            {
                var name = Path.GetFileNameWithoutExtension(file).ToLower();

                Templates[name].TableCreation = system.ReadStringFromFile(file);
            });
        }

        /// <summary>
        /// Read DDL templates from the application base directory
        /// </summary>
        public void ReadTemplates()
        {
            ReadTemplates(AppContext.BaseDirectory);
        }
    }

    public class DdlTemplate
    {
        private readonly string _name;

        public DdlTemplate(string name)
        {
            _name = name;
        }

        public string TableCreation { get; set; }
        public string FunctionCreation { get; set; }
    }
}