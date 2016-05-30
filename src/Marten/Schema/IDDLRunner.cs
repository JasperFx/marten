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

        public DDLRecorder(StringWriter writer)
        {
            _writer = writer;
        }

        public void Apply(object subject, string ddl)
        {
            _writer.WriteLine($"-- {subject}");
            _writer.WriteLine(ddl);
            _writer.WriteLine("");
            _writer.WriteLine("");
        }
    }
}