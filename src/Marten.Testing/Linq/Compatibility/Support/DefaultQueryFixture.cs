using Marten.Testing.Documents;

namespace Marten.Testing.Linq.Compatibility.Support
{
    public class DefaultQueryFixture: TargetSchemaFixture
    {
        public DefaultQueryFixture()
        {
            Store = provisionStore("linq_querying");

            DuplicatedFieldStore = provisionStore("duplicate_fields", o =>
            {
                o.Schema.For<Target>()
                    .Duplicate(x => x.Number)
                    .Duplicate(x => x.Long)
                    .Duplicate(x => x.String)
                    .Duplicate(x => x.Date)
                    .Duplicate(x => x.Double)
                    .Duplicate(x => x.Flag)
                    .Duplicate(x => x.Color);
            });
        }

        public DocumentStore DuplicatedFieldStore { get; set; }

        public DocumentStore Store { get; set; }
    }
}
