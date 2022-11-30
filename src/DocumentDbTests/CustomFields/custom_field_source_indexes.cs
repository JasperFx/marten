#nullable enable
using System.Linq;
using Marten;
using Marten.Linq;
using Marten.Schema;
using Marten.Testing.Harness;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace DocumentDbTests.CustomFields
{
    public class custom_field_source_indexes: IntegrationContext
    {
        private readonly ITestOutputHelper _output;

        public custom_field_source_indexes(DefaultStoreFixture fixture, ITestOutputHelper output):
            base(fixture)
        {
            _output = output;
            PostgresqlProvider.Instance.RegisterMapping(typeof(CustomId), "varchar",
                PostgresqlProvider.Instance.StringParameterType);
        }

        [Fact]
        public void can_add_duplicate_column_to_custom_id()
        {
            StoreOptions(_ =>
            {
                _.Linq.FieldSources.Add(new CustomIdFieldSource());
            });

            var queryPlan = theSession
                .Query<MyClassDuplicates>().OrderBy(x => x.CustomId).ThenBy(x => x.NullableCustomId).Explain();

            WriteQueryPlan(queryPlan);
        }


        [Fact]
        public void can_add_unique_column_to_custom_id()
        {
            StoreOptions(_ =>
            {
                _.Linq.FieldSources.Add(new CustomIdFieldSource());
            });

            var queryPlan = theSession
                .Query<MyClassUnique>().OrderBy(x => x.CustomIdComputed).ThenBy(x => x.CustomIdDuplicated).Explain();

            WriteQueryPlan(queryPlan);
        }

        private void WriteQueryPlan(QueryPlan queryPlan)
        {
            _output.WriteLine(queryPlan.Command.CommandText);
            foreach (var parameter in queryPlan.Command.Parameters.Where(p => p is not null))
                _output.WriteLine("{1} {0}: {2}", parameter.ParameterName, parameter.DbType, parameter.NpgsqlValue);
        }

        public class MyClassDuplicates
        {
            public string Id { get; set; }

            [DuplicateField]
            public CustomId CustomId { get; set; }

            [DuplicateField]
            public CustomId? NullableCustomId { get; set; }
        }

        public class MyClassUnique
        {
            public string Id { get; set; }

            [UniqueIndex(IndexType = UniqueIndexType.Computed)]
            public CustomId CustomIdComputed { get; set; }

            [UniqueIndex(IndexType = UniqueIndexType.DuplicatedField)]
            public CustomId? CustomIdDuplicated { get; set; }
        }
    }
}
