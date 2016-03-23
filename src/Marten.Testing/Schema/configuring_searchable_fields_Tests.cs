using System;
using System.Linq;
using Baseline;
using Marten.Schema;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema
{
    public class configuring_searchable_fields_Tests
    {
        [Fact]
        public void use_the_default_pg_type_for_the_member_type_if_not_overridden()
        {
            var mapping = DocumentMappingFactory.For<Organization>();
            var duplicate = mapping.DuplicatedFields.Single(x => x.MemberName == "Time2");

            duplicate.PgType.ShouldBe("timestamp without time zone");
        }

        [Fact]
        public void can_override_field_type_selection_on_the_attribute()
        {
            var mapping = DocumentMappingFactory.For<Organization>();
            var duplicate = mapping.DuplicatedFields.Single(x => x.MemberName == "Time");

            duplicate.PgType.ShouldBe("timestamp");
        }

        [Fact]
        public void can_override_with_MartenRegistry()
        {
            var registry = new MartenRegistry();
            registry.For<Organization>().Searchable(x => x.Time2, pgType:"timestamp");

            var schema = new DocumentSchema(new StoreOptions(), null, new NulloMartenLogger());
            schema.Alter(registry);

            schema.MappingFor(typeof(Organization)).As<DocumentMapping>().DuplicatedFields.Single(x => x.MemberName == "Time2")
                .PgType.ShouldBe("timestamp");
        }

        [PropertySearching(PropertySearching.JSON_Locator_Only)]
        public class Organization
        {
            public Guid Id { get; set; }

            [Searchable]
            public string Name { get; set; }

            [Searchable]
            public string OtherName;

            [Searchable(PgType = "timestamp")]
            public DateTime Time { get; set; }

            [Searchable]
            public DateTime Time2 { get; set; }

            public string OtherProp;
            public string OtherField { get; set; }
        }
    }
}