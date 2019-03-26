namespace Marten.Testing.Linq.Compatibility.Support
{
    public class DefaultQueryFixture : TargetSchemaFixture
    {
        public DefaultQueryFixture()
        {
            Store = provisionStore("linq_querying");
        }

        public DocumentStore Store { get; set; }
    }
}