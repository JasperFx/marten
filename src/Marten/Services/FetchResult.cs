using System;

namespace Marten.Services
{
    public class FetchResult<T>
    {
        public FetchResult(T document, string json, Guid? version)
        {
            Document = document;
            Json = json;
            Version = version;
        }

        public T Document { get; }
        public string Json { get; }
        public Guid? Version { get; }
    }
}