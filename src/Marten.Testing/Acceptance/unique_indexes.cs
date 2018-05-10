using System;
using Marten.Schema;
using Marten.Testing.Documents;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class unique_indexes : IntegratedFixture
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

                // This creates
                _.Schema.For<User>().UniqueIndex(UniqueIndexType.DuplicatedField, x => x.FirstName, x => x.FullName);
            });
            // ENDSAMPLE
        }
    }
}