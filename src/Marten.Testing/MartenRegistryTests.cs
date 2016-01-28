using System;
using System.Linq;
using Fixie;
using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;
using StructureMap;
using Xunit;

namespace Marten.Testing
{
    public class MartenRegistryTests
    {
        private DocumentSchema theSchema;
        
        public MartenRegistryTests()
        {
            theSchema = Container.For<DevelopmentModeRegistry>().GetInstance<DocumentSchema>();

            theSchema.Alter<TestRegistry>();
        }

        [Fact]
        public void property_searching_override()
        {
            theSchema.MappingFor(typeof(User))
                .PropertySearching.ShouldBe(PropertySearching.JSON_Locator_Only);
        }

        [Fact]
        public void picks_up_searchable_on_property()
        {
            theSchema.MappingFor(typeof (Organization))
                .FieldFor(nameof(Organization.Name)).ShouldBeOfType<DuplicatedField>();
        }

        [Fact]
        public void picks_up_searchable_on_field()
        {
            theSchema.MappingFor(typeof(Organization))
                .FieldFor(nameof(Organization.OtherName)).ShouldBeOfType<DuplicatedField>();
        }

        [Fact]
        public void searchable_field_is_also_indexed()
        {
            var mapping = theSchema.MappingFor(typeof (Organization));

            var index = mapping.IndexesFor("name").Single();
            index.IndexName.ShouldBe("mt_doc_organization_idx_name");
            index.Columns.ShouldHaveTheSameElementsAs("name");
        }


        [Fact]
        public void can_customize_the_index_on_a_searchable_field()
        {
            var mapping = theSchema.MappingFor(typeof(Organization));

            var index = mapping.IndexesFor("other_name").Single();
            index.IndexName.ShouldBe("mt_special");
            index.Columns.ShouldHaveTheSameElementsAs("other_name");
        }

        [Fact]
        public void can_set_up_gin_index_on_json_data()
        {
            var mapping = theSchema.MappingFor(typeof(Organization));

            var index = mapping.IndexesFor("data").Single();

            index.IndexName.ShouldBe("my_gin_index");
            index.Method.ShouldBe(IndexMethod.gin);
            index.Expression.ShouldBe("? jsonb_path_ops");
        }

        [Fact]
        public void mapping_is_set_to_containment_if_gin_index_is_added()
        {
            var mapping = theSchema.MappingFor(typeof(Organization));
            mapping.PropertySearching.ShouldBe(PropertySearching.ContainmentOperator);
        }

        public class TestRegistry : MartenRegistry
        {
            public TestRegistry()
            {
                For<Organization>()
                    .Searchable(x => x.Name).Searchable(x => x.OtherName, configure:x =>
                    {
                        x.IndexName = "mt_special";
                    })
                    .GinIndexJsonData(x => x.IndexName = "my_gin_index");

                For<User>().PropertySearching(PropertySearching.JSON_Locator_Only);
            }
        }

        public class Organization
        {
            public Guid Id { get; set; }

            public string Name { get; set; }

            public string OtherName;

            public string OtherProp;
            public string OtherField { get; set; }
        }
    }
}