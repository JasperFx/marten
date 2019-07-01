using System;
using System.Linq;
using Marten.Testing.Schema.Identity.Sequences;
using Xunit;

namespace Marten.Testing.Schema
{
    public class auto_create_schema_should_ensure_storage_only_once
    {
        // This test looks a bit funny, as the path that registeres a feature checked
        // in case of no schema patches does not log anything. Exposing something in ITenant
        // just for testing does not feel right either.
        [Fact]
        public void EnsureFeatureIsRecordedAsCheckedOnCreation()
        {
            var dllLog = new DdlLogger();
            using (var store = DocumentStore.For(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
                _.Connection(ConnectionSource.ConnectionString);
                _.Logger(dllLog);
            }))
            {
                store.Advanced.Clean.CompletelyRemoveAll();

                using (var store2 = DocumentStore.For(_ =>
                {
                    _.Connection(ConnectionSource.ConnectionString);
                }))
                {
                    // Store issues a command to verify & create event store schemas
                    using (var s = store.OpenSession())
                    {
                        s.Events.FetchStreamState(Guid.NewGuid());
                    }

                    // Second store clears schemas (needs the second store as cleanup clears schema checks too)
                    store2.Advanced.Clean.CompletelyRemoveAll();

                    // Path to check schemas should not be executed -> exception
                    var e = Assert.Throws<Marten.Exceptions.MartenCommandException>(() =>
                    {
                        using (var s = store.OpenSession())
                        {
                            s.Events.FetchStreamState(Guid.NewGuid());
                        }
                    });

                    Assert.Contains("relation \"public.mt_streams\" does not exist", e.Message);
                    // We should have enabled the feature, i.e. also generated & executed DDL
                    Assert.True(dllLog.Sql.Any(x => x.IndexOf("mt_append_event") > -1));
                }
            }
        }

        [Fact]
        public void EnsureCheckCanBeRemoved()
        {
            var dllLog = new DdlLogger();
            using (var store = DocumentStore.For(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
                _.Connection(ConnectionSource.ConnectionString);
                _.Logger(dllLog);
            }))
            {
                store.Advanced.Clean.CompletelyRemoveAll();

                using (var s = store.OpenSession())
                {
                    s.Events.FetchStreamState(Guid.NewGuid());
                }

                using (var store2 = DocumentStore.For(_ =>
                {
                    _.Connection(ConnectionSource.ConnectionString);
                }))
                {
                    store2.Advanced.Clean.CompletelyRemoveAll();
                }

                store.Tenancy.Default.ResetSchemaExistenceChecks();
                using (var s = store.OpenSession())
                {
                    s.Events.FetchStreamState(Guid.NewGuid());
                }

                // We have created mt_append_event more than once
                Assert.True(dllLog.Sql.Count(x => x.IndexOf("mt_append_event") > -1) > 1);
            }
        }
    }
}
