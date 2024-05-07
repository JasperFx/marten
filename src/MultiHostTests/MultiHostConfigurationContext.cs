using Marten;
using Marten.Internal.CodeGeneration;
using Marten.Testing.Harness;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;

namespace MultiHostTests
{
    /// <summary>
    /// Use this if the tests in a fixture are going to use
    /// all custom StoreOptions configuration
    /// </summary>
    [Collection("OneOffs")]
    public abstract class MultiHostConfigurationContext: IDisposable
    {
        protected readonly string _schemaName;
        private DocumentStore _store;
        private IDocumentSession _session;
        protected readonly IList<IDisposable> _disposables = new List<IDisposable>();
        private readonly string ConnectionString = "Host=localhost:5440,localhost:5441;Database=marten_testing;Username=user;password=password;Command Timeout=5";

        public string SchemaName => _schemaName;

        protected MultiHostConfigurationContext()
        {
            _schemaName = GetType().Name.ToLower().Sanitize();
        }

        public IList<IDisposable> Disposables => _disposables;

        protected DocumentStore StoreOptions(Action<StoreOptions> configure, bool cleanAll = true)
        {
            var options = new StoreOptions();
            var host = new NpgsqlDataSourceBuilder(ConnectionString).BuildMultiHost();
            options.Connection(host);

            options.Advanced.MultiHostSettings.ReadSessionPreference = TargetSessionAttributes.Standby;
            options.Advanced.MultiHostSettings.WriteSessionPreference = TargetSessionAttributes.Primary;

            // Can be overridden
            options.AutoCreateSchemaObjects = AutoCreate.All;
            options.NameDataLength = 100;
            options.DatabaseSchemaName = _schemaName;

            configure(options);

            if (cleanAll)
            {
                using var conn = host.CreateConnection(TargetSessionAttributes.Primary);
                conn.Open();
                conn.CreateCommand($"drop schema if exists {_schemaName} cascade")
                    .ExecuteNonQuery();
            }

            _store = new DocumentStore(options);

            _disposables.Add(_store);
            _disposables.Add(host);

            return _store;
        }

        protected DocumentStore theStore
        {
            get
            {
                if (_store == null)
                {
                    StoreOptions(_ => { });
                }

                return _store;
            }
            set
            {
                _store = value;
            }
        }

        protected IDocumentSession theSession
        {
            get
            {
                if (_session != null)
                    return _session;

                _session = theStore.LightweightSession();
                _disposables.Add(_session);

                return _session;
            }
        }

        public void Dispose()
        {
            foreach (var disposable in _disposables)
            {
                disposable.Dispose();
            }
        }

        protected Task AppendEvent(Guid streamId, params object[] events)
        {
            theSession.Events.Append(streamId, events);
            return theSession.SaveChangesAsync();
        }
    }
}
