using System;
using Marten.Schema;
using Marten.Schema.Indexing.Unique;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Acceptance
{
    [Collection("unique_text")]
    public class unique_indexes: IntegrationContext
    {
        // SAMPLE: using_a_single_property_computed_unique_index_through_attribute
        public class Account
        {
            public Guid Id { get; set; }

            [UniqueIndex(IndexType = UniqueIndexType.Computed)]
            public string Number { get; set; }
        }

        // ENDSAMPLE

        // SAMPLE: using_a_single_property_duplicate_field_unique_index_through_store_attribute
        public class Client
        {
            public Guid Id { get; set; }

            [UniqueIndex(IndexType = UniqueIndexType.DuplicatedField)]
            public string Name { get; set; }
        }

        // ENDSAMPLE

        // SAMPLE: using_a_multiple_properties_computed_unique_index_through_store_attribute
        public class Address
        {
            private const string UniqueIndexName = "sample_uidx_person";

            public Guid Id { get; set; }

            [UniqueIndex(IndexType = UniqueIndexType.Computed, IndexName = UniqueIndexName)]
            public string Street { get; set; }

            [UniqueIndex(IndexType = UniqueIndexType.Computed, IndexName = UniqueIndexName)]
            public string Number { get; set; }
        }

        // ENDSAMPLE

        // SAMPLE: using_a_multiple_properties_duplicate_field_unique_index_through_attribute
        public class Person
        {
            private const string UniqueIndexName = "sample_uidx_person";

            public Guid Id { get; set; }

            [UniqueIndex(IndexType = UniqueIndexType.DuplicatedField, IndexName = UniqueIndexName)]
            public string FirstName { get; set; }

            [UniqueIndex(IndexType = UniqueIndexType.DuplicatedField, IndexName = UniqueIndexName)]
            public string SecondName { get; set; }
        }

        // ENDSAMPLE

        [Fact]
        public void example_using_a_single_property_computed_unique_index()
        {
            // SAMPLE: using_a_single_property_computed_unique_index_through_store_options
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.DatabaseSchemaName = "unique_text";

                // This creates
                _.Schema.For<User>().UniqueIndex(UniqueIndexType.Computed, x => x.UserName);
            });
            // ENDSAMPLE
        }

        [Fact]
        public void example_using_a_single_property_duplicate_field_unique_index()
        {
            // SAMPLE: using_a_single_property_duplicate_field_unique_index_through_store_options
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.DatabaseSchemaName = "unique_text";

                // This creates
                _.Schema.For<User>().UniqueIndex(UniqueIndexType.DuplicatedField, x => x.UserName);
            });
            // ENDSAMPLE
        }

        [Fact]
        public void example_using_a_multiple_properties_computed_unique_index()
        {
            // SAMPLE: using_a_multiple_properties_computed_unique_index_through_store_options
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.DatabaseSchemaName = "unique_text";

                // This creates
                _.Schema.For<User>().UniqueIndex(UniqueIndexType.Computed, x => x.FirstName, x => x.FullName);
            });
            // ENDSAMPLE
        }

        [Fact]
        public void example_using_a_multiple_properties_duplicate_field_unique_index()
        {
            // SAMPLE: using_a_multiple_properties_duplicate_field_unique_index_through_store_options
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.DatabaseSchemaName = "unique_text";

                // This creates
                _.Schema.For<User>().UniqueIndex(UniqueIndexType.DuplicatedField, x => x.FirstName, x => x.FullName);
            });
            // ENDSAMPLE
        }

        [Fact]
        public void example_using_a_per_tenant_scoped_unique_index()
        {
            // SAMPLE: per-tenant-unique-index
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.DatabaseSchemaName = "unique_text";

                // This creates a duplicated field unique index on firstname, lastname and tenant_id
                _.Schema.For<User>().MultiTenanted().UniqueIndex(UniqueIndexType.DuplicatedField, "index_name", TenancyScope.PerTenant, x => x.FirstName, x => x.LastName);

                // This creates a computed unique index on client name and tenant_id
                _.Schema.For<Client>().MultiTenanted().UniqueIndex(UniqueIndexType.Computed, "index_name", TenancyScope.PerTenant, x => x.Name);
            });
            // ENDSAMPLE
        }

        public unique_indexes(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
