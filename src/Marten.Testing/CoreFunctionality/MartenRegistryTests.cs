using System;
using System.Linq;
using Baseline;
using Marten.Linq.Fields;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql.Tables;
using Xunit;

namespace Marten.Testing.CoreFunctionality
{
    [Collection("martenregistry")]
    public class MartenRegistryTests : OneOffConfigurationsContext
    {
        private readonly StorageFeatures theStorage;

        public MartenRegistryTests() : base("registry")
        {
            var store = SeparateStore(_ =>
            {
                _.Schema.For<Organization>()
                    .Duplicate(x => x.Name).Duplicate(x => x.OtherName, configure: x =>
                    {
                        x.Name = "mt_special";
                    })
                    .GinIndexJsonData(x => x.Name = "my_gin_index")
                    .IndexLastModified(x => x.IsConcurrent = true)
                    .SoftDeletedWithIndex(x => x.Method = IndexMethod.brin);

                _.Schema.For<User>().PropertySearching(PropertySearching.JSON_Locator_Only);
            });

            theStorage = store.Storage;
        }

        [Fact]
        public void property_searching_override()
        {
            theStorage.MappingFor(typeof(User)).As<DocumentMapping>()
                .PropertySearching.ShouldBe(PropertySearching.JSON_Locator_Only);
        }

        [Fact]
        public void picks_up_searchable_on_property()
        {
            theStorage.MappingFor(typeof (Organization)).As<DocumentMapping>()
                .FieldFor(nameof(Organization.Name)).ShouldBeOfType<DuplicatedField>();
        }

        [Fact]
        public void picks_up_searchable_on_field()
        {
            theStorage.MappingFor(typeof(Organization)).As<DocumentMapping>()
                .FieldFor(nameof(Organization.OtherName)).ShouldBeOfType<DuplicatedField>();
        }

        [Fact]
        public void searchable_field_is_also_indexed()
        {
            var mapping = theStorage.MappingFor(typeof (Organization)).As<DocumentMapping>();

            var index = mapping.IndexesFor("name").Single();
            index.Name.ShouldBe("mt_doc_martenregistrytests_organization_idx_name");
            index.Columns.ShouldHaveTheSameElementsAs("name");
        }


        [Fact]
        public void can_customize_the_index_on_a_searchable_field()
        {
            var mapping = theStorage.MappingFor(typeof(Organization)).As<DocumentMapping>();

            var index = mapping.IndexesFor("other_name").Single();
            index.Name.ShouldBe("mt_special");
            index.Columns.ShouldHaveTheSameElementsAs("other_name");
        }

        [Fact]
        public void can_set_up_gin_index_on_json_data()
        {
            var mapping = theStorage.MappingFor(typeof(Organization)).As<DocumentMapping>();

            var index = mapping.IndexesFor("data").Single();

            index.Name.ShouldBe("my_gin_index");
            index.Method.ShouldBe(IndexMethod.gin);
            index.Expression.ShouldBe("(? jsonb_path_ops)");
        }

        [Fact]
        public void mapping_is_set_to_containment_if_gin_index_is_added()
        {
            var mapping = theStorage.MappingFor(typeof(Organization)).As<DocumentMapping>();
            mapping.PropertySearching.ShouldBe(PropertySearching.ContainmentOperator);
        }

        [Fact]
        public void mt_last_modified_index_is_added()
        {
            var mapping = theStorage.MappingFor(typeof(Organization)).As<DocumentMapping>();

            var index = mapping.IndexesFor(SchemaConstants.LastModifiedColumn).Single();

            index.IsConcurrent.ShouldBe(true);
        }

        [Fact]
        public void mt_deleted_at_index_is_added()
        {
            var mapping = theStorage.MappingFor(typeof(Organization)).As<DocumentMapping>();

            var index = mapping.IndexesFor(SchemaConstants.DeletedAtColumn).Single();

            var ddl = index.ToDDL(new DocumentTable(mapping));
            ddl
                .ShouldContain("WHERE (mt_deleted)", Case.Insensitive);

            index.Method.ShouldBe(IndexMethod.brin);
        }


        public class Organization
        {
            public Guid Id { get; set; }

            public string Name { get; set; }

            public string OtherName;

            public string OtherProp;
            public string OtherField { get; set; }
        }

        public class OrganizationRegistry: MartenRegistry
        {
            public OrganizationRegistry()
            {
                For<Organization>().Duplicate(x => x.OtherName);
                For<User>().Duplicate(x => x.UserName);
            }
        }

        [Fact]
        public void using_registry_include()
        {
            var store = DocumentStore.For(opts =>
            {
                opts.Schema.For<Organization>().Duplicate(x => x.Name);
                opts.Schema.Include<OrganizationRegistry>();
                opts.Connection(ConnectionSource.ConnectionString);
            });

            var organizations = store
                .Options.Storage.MappingFor(typeof(Organization));

            organizations.DuplicatedFields.Any(x => x.MemberName == nameof(Organization.OtherName))
                .ShouldBeTrue();

            organizations.DuplicatedFields.Any(x => x.MemberName == nameof(Organization.Name))
                .ShouldBeTrue();

            store.Options.Storage.MappingFor(typeof(User)).DuplicatedFields
                .Single().MemberName.ShouldBe(nameof(User.UserName));
        }
    }
}
