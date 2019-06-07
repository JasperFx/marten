using System;

namespace Marten.Schema
{
    public class ActualForeignKey
    {
        public DbObjectName Table { get; }
        public string Name { get; }
        public string DDL { get; }

        public bool DoesCascadeDeletes()
        {
            // NOTE: Use .IndexOf() so it's not effected by whitespace
            return DDL.IndexOf("on delete cascade", StringComparison.OrdinalIgnoreCase) != -1;
        }

        public ActualForeignKey(DbObjectName table, string name, string ddl)
        {
            Table = table;
            Name = name;
            DDL = ddl;
        }
    }
}