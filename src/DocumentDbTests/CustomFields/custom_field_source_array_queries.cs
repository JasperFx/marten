#nullable enable
using System.Linq;
using Marten;
using Marten.Linq;
using Marten.Testing.Harness;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace DocumentDbTests.CustomFields
{
    public class custom_field_source_array_queries: IntegrationContext
    {
        private readonly ITestOutputHelper _output;

        public custom_field_source_array_queries(DefaultStoreFixture fixture, ITestOutputHelper output):
            base(fixture)
        {
            _output = output;

            PostgresqlProvider.Instance.RegisterMapping(typeof(CustomId), "varchar",
                PostgresqlProvider.Instance.StringParameterType);
            StoreOptions(_ =>
            {
                _.Linq.FieldSources.Add(new CustomIdFieldSource());
            });
        }

        [Fact]
        public void can_query_array_any()
        {
            var queryPlan = theSession
                .Query<MyClassArray>()
                .Where(x => x.CustomIds.Any()).Explain();

            WriteQueryPlan(queryPlan);
        }

        [Fact]
        public void can_query_array_length_equals()
        {
            var queryPlan = theSession
                .Query<MyClassArray>()
                .Where(x => x.CustomIds.Length == 1).Explain();

            WriteQueryPlan(queryPlan);
        }

        [Fact]
        public void can_query_array_count_equals()
        {
            var queryPlan = theSession
                .Query<MyClassArray>()
                .Where(x => x.CustomIds.Count() == 1).Explain();

            WriteQueryPlan(queryPlan);
        }

        [Fact]
        public void can_query_array_contains()
        {
            var testValue = new CustomId("test");
            var queryPlan = theSession
                .Query<MyClassArray>()
                .Where(x => x.CustomIds.Contains(testValue)).Explain();

            WriteQueryPlan(queryPlan);
        }


        [Fact]
        public void can_query_array_not_contains()
        {
            var testValue = new CustomId("test");
            var queryPlan = theSession
                .Query<MyClassArray>()
                .Where(x => !x.CustomIds.Contains(testValue)).Explain();

            WriteQueryPlan(queryPlan);
        }

        [Fact]
        public void can_query_array_any_with_predicate()
        {
            var testValue = new CustomId("test");
            var queryPlan = theSession
                .Query<MyClassArray>()
                .Where(x => x.CustomIds.Any(_ => _ == testValue)).Explain();

            WriteQueryPlan(queryPlan);
        }

        [Fact]
        public void can_query_array_not_any()
        {
            var testValue = new CustomId("test");
            var queryPlan = theSession
                .Query<MyClassArray>()
                // ReSharper disable once SimplifyLinqExpressionUseAll
                .Where(x => !x.CustomIds.Any(_ => _ == testValue)).Explain();

            WriteQueryPlan(queryPlan);
        }

        [Fact(Skip = "All is not supported")]
        public void can_query_array_all_not()
        {
            var testValue = new CustomId("test");
            var queryPlan = theSession
                .Query<MyClassArray>()
                .Where(x => x.CustomIds.All(_ => _ != testValue)).Explain();

            WriteQueryPlan(queryPlan);
        }


        [Fact(Skip = "All is not supported")]
        public void can_query_array_all_equal()
        {
            var testValue = new CustomId("test");
            var queryPlan = theSession
                .Query<MyClassArray>()
                .Where(x => x.CustomIds.All(_ => _ == testValue)).Explain();

            WriteQueryPlan(queryPlan);
        }

        [Fact]
        public void can_query_array_is_one_of()
        {
            var testValues = new[] {new CustomId("test1"), new CustomId("test2")};
            var queryPlan = theSession
                .Query<MyClassArray>()
                .Where(x => x.CustomIds.IsOneOf(testValues)).Explain();

            WriteQueryPlan(queryPlan);
        }


        [Fact]
        public void can_query_array_is_superset_of()
        {
            var testValues = new[] {new CustomId("test1"), new CustomId("test2")};
            var queryPlan = theSession
                .Query<MyClassArray>()
                .Where(x => x.CustomIds.IsSubsetOf(testValues)).Explain();

            WriteQueryPlan(queryPlan);
        }


        [Fact]
        public void can_query_array_is_subset_of()
        {
            var testValues = new[] {new CustomId("test1"), new CustomId("test2")};
            var queryPlan = theSession
                .Query<MyClassArray>()
                .Where(x => x.CustomIds.IsSubsetOf(testValues)).Explain();

            WriteQueryPlan(queryPlan);
        }

        private void WriteQueryPlan(QueryPlan queryPlan)
        {
            _output.WriteLine(queryPlan.Command.CommandText);
            foreach (var parameter in queryPlan.Command.Parameters.Where(p => p is not null))
                _output.WriteLine("{1} {0}: {2}", parameter.ParameterName, parameter.DbType, parameter.NpgsqlValue);
        }

        public class MyClassArray
        {
            public string Id { get; set; }
            public CustomId[] CustomIds { get; set; }
        }
    }
}
