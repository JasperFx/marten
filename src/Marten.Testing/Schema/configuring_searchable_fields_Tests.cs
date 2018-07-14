using System;
using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Util;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema
{
    public class configuring_searchable_fields_Tests
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
            var indexDefinition = mapping.Indexes.Cast<IndexDefinition>().Single(x => x.Columns.First() == "Name".ToTableAlias());

            indexDefinition.Method.ShouldBe(IndexMethod.btree);
        }

        [Fact]
        public void can_override_index_type_and_name_on_the_attribute()
        {
            var mapping = DocumentMapping.For<Organization>();
            var indexDefinition = (IndexDefinition)mapping.Indexes.Single(x => x.IndexName == "mt_idx_foo");

            indexDefinition.Method.ShouldBe(IndexMethod.hash);
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
            var store = TestingDocumentStore.For(_ =>
            {
                _.Schema.For<Organization>().Duplicate(x => x.Time2, pgType: "timestamp");
            });

            store.Storage.MappingFor(typeof(Organization)).As<DocumentMapping>().DuplicatedFields.Single(x => x.MemberName == "Time2")
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

            [DuplicateField(PgType = "timestamp")]
            public DateTime Time { get; set; }

            [DuplicateField]
            public DateTime Time2 { get; set; }

            public string OtherProp;
            public string OtherField { get; set; }
        }
    }
}