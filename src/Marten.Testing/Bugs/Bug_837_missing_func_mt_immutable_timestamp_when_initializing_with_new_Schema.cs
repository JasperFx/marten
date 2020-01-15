using System;
using System.Linq;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_837_missing_func_mt_immutable_timestamp_when_initializing_with_new_Schema: IntegratedFixture
    {
        [Fact]
        public void missing_func_mt_immutable_timestamp_when_initializing_with_new_Schema()
        {
            var store = DocumentStore.For(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.DatabaseSchemaName = "other1";
                _.Connection(ConnectionSource.ConnectionString);
            });

            using (var session = store.OpenSession())
            {
                session.Query<Target>().FirstOrDefault(m => m.DateOffset > DateTimeOffset.Now);
            }
        }

        [Fact]
        public void test_func_mt_immutable_timestamp_when_initializing_with_default_Schema()
        {
            var store = DocumentStore.For(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Connection(ConnectionSource.ConnectionString);
            });

            using (var session = store.OpenSession())
            {
                session.Query<Target>().FirstOrDefault(m => m.DateOffset > DateTimeOffset.Now);
            }
        }
    }
}
