using System;

namespace Marten.Schema
{
    /// <summary>
    /// Overrides the database schema name for the document type
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class DatabaseSchemaNameAttribute : MartenAttribute
    {
        private readonly string _name;

        public DatabaseSchemaNameAttribute(string name)
        {
            _name = name;
        }

        public override void Modify(DocumentMapping mapping)
        {
            mapping.DatabaseSchemaName = _name;
        }
    }
}