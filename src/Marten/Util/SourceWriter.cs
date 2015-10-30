using System.IO;

namespace Marten.Util
{
    public class SourceWriter
    {
        private readonly StringWriter _writer = new StringWriter();

        private int _level = 0;
        private string _leadingSpaces = "";

        public int IndentionLevel
        {
            get { return _level; }
            set
            {
                _level = value;
                _leadingSpaces = "".PadRight(_level*4);
            }
        }

        public void WriteLine(string text)
        {
            _writer.WriteLine(_leadingSpaces + text);
        }

        

        public void StartNamespace(string @namespace)
        {
            WriteLine($"namespace {@namespace}");
            StartBlock();
        }

        private void StartBlock()
        {
            WriteLine("{");
            IndentionLevel++;
        }

        public void FinishBlock()
        {
            IndentionLevel--;
            WriteLine("}");
        }

        public string Code()
        {
            return _writer.ToString();
        }
    }
}