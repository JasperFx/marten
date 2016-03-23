using Marten.Schema;

namespace Marten.Testing
{
    public static class DocumentMappingFactory
    {
        public static DocumentMapping For<T>()
        {
            return new DocumentMapping(typeof(T), new StoreOptions());
        }
    }
}