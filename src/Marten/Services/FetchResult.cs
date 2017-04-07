using System;
using System.IO;

namespace Marten.Services
{
    [Obsolete("try to eliminate this")]
    public class FetchResult<T>
    {
        public FetchResult(T document, TextReader json, Guid? version)
        {
            Document = document;
            Json = json;
            Version = version;
        }

        public T Document { get; }
        public TextReader Json { get; }
        public Guid? Version { get; }
    }
}