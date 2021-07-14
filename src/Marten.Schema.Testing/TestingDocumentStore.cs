using System;
using Baseline;
using Weasel.Postgresql;
using Marten.Testing.Harness;
using Npgsql;
using Xunit.Abstractions;

namespace Marten.Schema.Testing
{

    public class TestingDocumentStore : DocumentStore
    {
        public static int SchemaCount = 0;
        private static readonly object _locker = new object();

        public new static DocumentStore For(Action<StoreOptions> configure)
        {
            var options = new StoreOptions();
            options.Connection(ConnectionSource.ConnectionString);
	        options.AutoCreateSchemaObjects = AutoCreate.All;
			options.NameDataLength = 100;

            configure(options);



            var store = new TestingDocumentStore(options);
            store.Advanced.Clean.CompletelyRemoveAll();

            return store;
        }


        public static DocumentStore Basic(ITestOutputHelper output = null)
        {
            return For(_ =>
            {
                if (output != null)
                    _.Logger(new TestOutputMartenLogger(output));
            }).As<DocumentStore>();
        }

        public static DocumentStore DefaultSchema(ITestOutputHelper output = null)
        {
            var store = For(_ =>
            {
                if (output != null)
                    _.Logger(new TestOutputMartenLogger(output));
                _.DatabaseSchemaName = SchemaConstants.DefaultSchema;
            });
            return store;
        }

        private TestingDocumentStore(StoreOptions options) : base(options)
        {
        }

        public override void Dispose()
        {
            var schemaName = Options.DatabaseSchemaName;

            if (schemaName != SchemaConstants.DefaultSchema)
            {
                var sql = $"DROP SCHEMA {schemaName} CASCADE;";
                var cmd = new NpgsqlCommand(sql);
                using (var conn = Tenancy.Default.OpenConnection())
                {
                    conn.Execute(cmd);
                }
            }


            base.Dispose();


        }
    }
}
