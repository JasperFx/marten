using System.IO;
using System.Text;

namespace MartenBenchmarks.Infrastructure
{
    public static class StringToTextReaderExtensions
    {
        public static TextReader ToReader(this string json)
        {
            return new StringReader(json);
        }
    }

    public static class StringToStreamExtensions
    {
        public static Stream ToReader(this string json)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(json));
        }
    }
}
