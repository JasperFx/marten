using Marten.Schema;

namespace Marten.Testing.Harness
{
    public class DefaultStoreFixture: StoreFixture
    {
        public DefaultStoreFixture() : base(SchemaConstants.DefaultSchema)
        {

        }
    }
}
