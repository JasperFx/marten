using System.IO;

namespace MartenBenchmarks.Infrastructure
{
    public static class StringToTextReaderExtensions
    {
        public static TextReader ToReader(this string json)
        {
            return new StringReader(json);
        }
    }
}