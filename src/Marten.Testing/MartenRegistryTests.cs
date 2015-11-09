using System;
using Marten.Schema;
using Shouldly;
using StructureMap;

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

        public void property_searching_override()
        {
            theSchema.MappingFor(typeof(Organization))
                .PropertySearching.ShouldBe(PropertySearching.JSON_Locator_Only);
        }

        public void picks_up_searchable_on_property()
        {
            theSchema.MappingFor(typeof (Organization))
                .FieldFor(nameof(Organization.Name)).ShouldBeOfType<DuplicatedField>();
        }

        public void picks_up_searchable_on_field()
        {
            theSchema.MappingFor(typeof(Organization))
                .FieldFor(nameof(Organization.OtherName)).ShouldBeOfType<DuplicatedField>();
        }


        public class TestRegistry : MartenRegistry
        {
            public TestRegistry()
            {
                For<Organization>().PropertySearching(PropertySearching.JSON_Locator_Only)
                    .Searchable(x => x.Name).Searchable(x => x.OtherName);
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