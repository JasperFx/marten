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
    public class custom_field_source_queries: IntegrationContext
    {
        private readonly ITestOutputHelper _output;

        public custom_field_source_queries(DefaultStoreFixture fixture, ITestOutputHelper output):
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
        public void can_query_by_custom_id()
        {
            var testValue = new CustomId("test");
            var queryPlan = theSession
                .Query<MyClass>()
                .Where(x => x.CustomId == testValue).Explain();

            WriteQueryPlan(queryPlan);
        }

        [Fact]
        public void can_load()
        {
            var docs = theSession.Load<MyClass>("SomeId");
            var docs2 = theSession.Load<MyClassNullable>("SomeId");
        }

        [Fact]
        public void can_load_many()
        {
            var docs = theSession.LoadMany<MyClass>("SomeId", "SomeOtherId");
            var docs2 = theSession.LoadMany<MyClassNullable>("SomeId", "SomeOtherId");
        }

        [Fact]
        public void can_query_is_one_of_custom_id_array()
        {
            var testValues = new[] {new CustomId("test1"), new CustomId("test2")};
            var queryPlan = theSession
                .Query<MyClass>()
                .Where(x => x.CustomId.IsOneOf(testValues)).Explain();

            WriteQueryPlan(queryPlan);
        }

        [Fact]
        public void can_query_by_nullable_custom_id()
        {
            var testValue = new CustomId("test");
            var queryPlan = theSession
                .Query<MyClassNullable>()
                .Where(x => x.CustomId == testValue || x.CustomId == null).Explain();

            WriteQueryPlan(queryPlan);
        }

        [Fact]
        public void can_query_null_custom_id()
        {
            var queryPlan = theSession
                .Query<MyClassNullable>()
                .Where(x => x.CustomId == null).Explain();

            WriteQueryPlan(queryPlan);
        }

        [Fact]
        public void can_query_by_array_custom_id()
        {
            var testValue = new CustomId("test");
            var queryPlan = theSession
                .Query<MyClassArray>()
                .Where(x => x.CustomIds.Length == 0 || x.CustomIds.Contains(testValue)).Explain();

            WriteQueryPlan(queryPlan);
        }

        private void WriteQueryPlan(QueryPlan queryPlan)
        {
            _output.WriteLine(queryPlan.Command.CommandText);
            foreach (var parameter in queryPlan.Command.Parameters.Where(p => p is not null))
                _output.WriteLine("{1} {0}: {2}", parameter.ParameterName, parameter.DbType, parameter.NpgsqlValue);
        }

        public class MyClass
        {
            public string Id { get; set; }
            public CustomId CustomId { get; set; }
        }

        public class MyClassNullable
        {
            public string Id { get; set; }
            public CustomId? CustomId { get; set; }
        }

        public class MyClassArray
        {
            public string Id { get; set; }
            public CustomId[] CustomIds { get; set; }
        }
    }
}
