using System.IO;

namespace Marten.Schema
{
    public interface IDDLRunner
    {
        void Apply(object subject, string ddl);
    }

    public class DDLRecorder : IDDLRunner
    {
        private readonly StringWriter _writer;

        public DDLRecorder() : this(new StringWriter())
        {
        }

        public DDLRecorder(StringWriter writer)
        {
            _writer = writer;
        }

        public StringWriter Writer => _writer;

        public void Apply(object subject, string ddl)
        {
            _writer.WriteLine($"-- {subject}");
            _writer.WriteLine(ddl);
            _writer.WriteLine("");
            _writer.WriteLine("");
        }
    }

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
    }
}