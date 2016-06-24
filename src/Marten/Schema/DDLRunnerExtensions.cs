using System;
using Baseline;

namespace Marten.Schema
{
    public static class DDLRunnerExtensions
    {
        public static void Drop(this IDDLRunner runner, object subject, TableName table)
        {
            var sql = $"drop table if exists {table.QualifiedName} cascade;";
            runner.Apply(subject, sql);
        }

        public static void RemoveColumn(this IDDLRunner runner, object subject, TableName table, string columnName)
        {
            var sql = $"alter table if exists {table.QualifiedName} drop column if exists {columnName};";

            runner.Apply(subject, sql);
        }

        public static void OwnershipToTable(this IDDLRunner runner, StoreOptions options, TableName table)
        {
            if (options.DatabaseOwnerName.IsNotEmpty())
            {
                runner.Apply(table, $"ALTER TABLE {table.QualifiedName} OWNER TO {options.DatabaseOwnerName};");
            }
        }

    }
}