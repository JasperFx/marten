using System;
using System.Linq;
using Baseline;
using Marten.Util;
using Shouldly;
using Weasel.Postgresql.Tables;
using Xunit;

namespace Marten.Schema.Testing
{
    public class configuring_searchable_fields_Tests : IntegrationContext
    {

        [Fact]
        public void use_the_default_pg_type_for_the_member_type_if_not_overridden()
        {
            var mapping = DocumentMapping.For<Organization>();
            var duplicate = mapping.DuplicatedFields.Single(x => x.MemberName == "Time2");

            duplicate.PgType.ShouldBe("timestamp without time zone");
        }

        [Fact]
        public void creates_btree_index_for_the_member()
        {
            var mapping = DocumentMapping.For<Organization>();
            var indexDefinition = mapping.Indexes.Cast<DocumentIndex>().Single(x => x.Columns.First() == "Name".ToTableAlias());

            indexDefinition.Method.ShouldBe(IndexMethod.btree);
        }

        [Fact]
        public void can_override_index_type_and_name_on_the_attribute()
        {
            var mapping = DocumentMapping.For<Organization>();
            var indexDefinition = (DocumentIndex)mapping.Indexes.Single(x => x.Name == "idx_foo");

            indexDefinition.Method.ShouldBe(IndexMethod.hash);
        }

        [Fact]
        public void can_override_index_sort_order_on_the_attribute()
        {
            var mapping = DocumentMapping.For<Organization>();
            var indexDefinition = mapping.Indexes.Cast<DocumentIndex>().Single(x => x.Columns.First() == "YetAnotherName".ToTableAlias());

            indexDefinition.SortOrder.ShouldBe(SortOrder.Desc);
        }

        [Fact]
        public void can_override_field_type_selection_on_the_attribute()
        {
            var mapping = DocumentMapping.For<Organization>();
            var duplicate = mapping.DuplicatedFields.Single(x => x.MemberName == "Time");

            duplicate.PgType.ShouldBe("timestamp");
        }

        [Fact]
        public void can_override_with_MartenRegistry()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Organization>().Duplicate(x => x.Time2, pgType: "timestamp");
            });

            theStore.Storage.MappingFor(typeof(Organization)).As<DocumentMapping>().DuplicatedFields.Single(x => x.MemberName == "Time2")
                .PgType.ShouldBe("timestamp");
        }

        [PropertySearching(PropertySearching.JSON_Locator_Only)]
        public class Organization
        {
            public Guid Id { get; set; }

            [DuplicateField]
            public string Name { get; set; }

            [DuplicateField(IndexMethod = IndexMethod.hash, IndexName = "idx_foo")]
            public string OtherName;

            [DuplicateField(IndexSortOrder = SortOrder.Desc)]
            public string YetAnotherName { get; set; }

            [DuplicateField(PgType = "timestamp")]
            public DateTime Time { get; set; }

            [DuplicateField]
            public DateTime Time2 { get; set; }

            public string OtherProp;
            public string OtherField { get; set; }
        }
    }
}
