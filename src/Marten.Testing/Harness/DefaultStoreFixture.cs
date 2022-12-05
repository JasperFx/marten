using System;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Util;
using Marten.Events;
using Npgsql;
using Weasel.Core;
using Xunit;

namespace Marten.Testing.Harness
{
    public class DefaultStoreFixture: IAsyncLifetime
    {
        public readonly Lazy<DocumentStore> StringStreamIdentifiers = new Lazy<DocumentStore>(() =>
        {
            var store = DocumentStore.For(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.AutoCreateSchemaObjects = AutoCreate.All;
                opts.DatabaseSchemaName = "string_events";
                opts.Events.StreamIdentity = StreamIdentity.AsString;

            });

            using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
            conn.Open();
            conn.CreateCommand("drop schema if exists string_events cascade");

            return store;
        });

        public Task InitializeAsync()
        {
            Store = DocumentStore.For(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.AutoCreateSchemaObjects = AutoCreate.All;

                opts.GeneratedCodeMode = TypeLoadMode.Auto;
                opts.ApplicationAssembly = GetType().Assembly;
            });

            // Do this exactly once and no more.
            return Store.Advanced.Clean.CompletelyRemoveAllAsync();
        }

        public DocumentStore Store { get; private set; }

        public Task DisposeAsync()
        {
            Store.Dispose();
            if (StringStreamIdentifiers.IsValueCreated)
            {
                StringStreamIdentifiers.Value.Dispose();
            }
            return Task.CompletedTask;
        }
    }
}
