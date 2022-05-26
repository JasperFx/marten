using System.Linq;
using Marten;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Configuration
{
    public class ignoring_indexes_on_document_table : OneOffConfigurationsContext
    {
        [Fact]
        public void index_is_ignored_on_document_table()
        {
            var mapping = DocumentMapping.For<User>();
            mapping.IgnoreIndex("foo");

            var table = new DocumentTable(mapping);
            table.IgnoredIndexes.Any(x => x == "foo").ShouldBeTrue();
        }

        [Fact]
        public void ignore_index_through_configuration()
        {
            var store = DocumentStore.For(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.Schema.For<User>().IgnoreIndex("foo");
            });

            var mapping = store.Options.Storage.MappingFor(typeof(User));
            new DocumentTable(mapping).IgnoredIndexes.Single().ShouldBe("foo");
        }
    }
}
