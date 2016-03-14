namespace Marten.Services
{
    public class FetchResult<T>
    {
        public FetchResult(T document, string json)
        {
            Document = document;
            Json = json;
        }

        public T Document { get; }

        public string Json { get; }
    }
}