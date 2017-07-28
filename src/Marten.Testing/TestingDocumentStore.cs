using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using System.Dynamic;
using System.IO;
using Baseline;
using Baseline.Dates;
using Marten;
using Marten.Schema;
using Marten.Testing.Documents;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Marten.Testing
{

    public class TestingDocumentStore : DocumentStore
    {
        public static int SchemaCount = 0;
        private static readonly object _locker = new object();

        private static readonly Dictionary<string, Action<StoreOptions>>
            CustomizationPerContract = new Dictionary<string, Action<StoreOptions>>
            {
                { TestingContracts.CamelCase, (options) =>
                {
                    var currentSerializer = options.Serializer();
                    options.UseDefaultSerialization(currentSerializer.EnumStorage, Casing.CamelCase);
                } }
            };

        public new static DocumentStore For(Action<StoreOptions> configure)
        {
            var options = new StoreOptions();
            options.Connection(ConnectionSource.ConnectionString);
            options.Serializer<TestsSerializer>();            
            options.NameDataLength = 100;            
            
            configure(options);

            ContractsPerEnvironment(options);
            
            var store = new TestingDocumentStore(options);
            store.Advanced.Clean.CompletelyRemoveAll();

            return store;
        }

        private static void ContractsPerEnvironment(StoreOptions options)
        {
            foreach (var c in CustomizationPerContract)
            {
                if (Environment.GetEnvironmentVariable(c.Key) != null)
                {
                    CustomizationPerContract[c.Key](options);
                }
            }
        }

        public static DocumentStore Basic()
        {
            return For(_ =>
            {
            }).As<DocumentStore>();
        }

        public static DocumentStore DefaultSchema()
        {
            var store = For(_ =>
            {
                _.DatabaseSchemaName = StoreOptions.DefaultDatabaseSchemaName;
            });
            return store;
        }

        private TestingDocumentStore(StoreOptions options) : base(options)
        {
        }

        public override void Dispose()
        {
            var schemaName = Options.DatabaseSchemaName;

            if (schemaName != StoreOptions.DefaultDatabaseSchemaName)
            {
                var sql = $"DROP SCHEMA {schemaName} CASCADE;";
                using (var conn = Tenancy.Default.OpenConnection())
                {
                    conn.Execute(cmd => cmd.CommandText = sql);
                }
            }


            base.Dispose();


        }
    }

    internal static class TestHelperExtensions
    {
        public static string Locator<T>(this IQuerySession session, Expression<Func<T, object>> e)
        {
            return session.DocumentStore.Advanced.Storage
                .MappingFor(typeof(T)).JsonLocator(e);                                        
        }
        
        public static string ColumnName<T>(this IQuerySession session, Expression<Func<T, object>> e)
        {
            return session.DocumentStore.Advanced.Storage
                .MappingFor(typeof(T)).FieldInfo(e).ColumnName;
        }


        public static string CaseBy(this string value, Casing casing)
        {
            var jsonSerializerSettings = new JsonSerializerSettings();

            if (casing == Casing.CamelCase)
            {
                jsonSerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();

            }

            var jsonSerializer = JsonSerializer.CreateDefault(jsonSerializerSettings);
            using (var sw = new StringWriter())
            using (var jsonWriter = new JsonTextWriterEx(sw))
            {                
                var interimObject = JsonConvert.DeserializeObject<ExpandoObject>(value);
                jsonSerializer.Serialize(jsonWriter, interimObject);
                return sw.ToString();
            }            
        }

        public class JsonTextWriterEx : JsonTextWriter
        {
            private readonly TextWriter _textWriter;            

            public JsonTextWriterEx(TextWriter textWriter) : base(textWriter)
            {
                _textWriter = textWriter;                
            }

            protected override void WriteValueDelimiter()
            {
                this._textWriter.Write(", ");
            }
            
            public override void WritePropertyName(string name, bool escape)
            {
                base.WritePropertyName(name, escape);
                _textWriter.Write(" ");
            }
        }
    }
}