using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Marten.Schema;
using Marten.Services;
using Marten.Testing.Documents;
using Npgsql;
using NpgsqlTypes;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema
{
    public class executing_upsert_with_char_buffer_pooling_enabled
    {
        [Fact]
        public void uses_buffers()
        {
            var releasedWriters = new List<CharArrayTextWriter>();
            var mapping = DocumentMapping.For<User>();
            var resolver = new Resolver<User>(new Serializer(), mapping, true);

            var connection = Substitute.For<IManagedConnection>();

            var pool = Substitute.For<CharArrayTextWriter.IPool>();
            pool.Lease().Returns(c => new CharArrayTextWriter());
            pool.WhenForAnyArgs(p => p.Release(default(IEnumerable<CharArrayTextWriter>))).
                Do(ci => releasedWriters.AddRange((IEnumerable<CharArrayTextWriter>) ci.Args()[0]));

            var batch = new UpdateBatch(new StoreOptions(), new Serializer(), connection, new VersionTracker(), pool);
            resolver.RegisterUpdate(batch, new User { FirstName = "Bruce" });
            batch.Execute();

            var json = GetJsonBParameter(connection);
            var jsonContent = new string( (char[]) json.Value,0, json.Size);

            jsonContent.ShouldBe(Serializer.WriterValue);
            releasedWriters.ShouldHaveSingleItem();
        }

        static NpgsqlParameter GetJsonBParameter(IManagedConnection connection)
        {
            var call = connection.ReceivedCalls().Single();
            var cmd = (NpgsqlCommand) call.GetArguments()[0];
            var json = cmd.Parameters.Single(p => p.NpgsqlDbType == NpgsqlDbType.Jsonb);
            return json;
        }

        class Serializer : ISerializer
        {
            public const string WriterValue = "ThisIsWriterValue";

            public void ToJson(object document, TextWriter writer)
            {
                writer.Write(WriterValue);
            }

            public string ToJson(object document)
            {
                return ""; //dummy
            }

            public T FromJson<T>(string json)
            {
                throw new NotImplementedException();
            }

            public object FromJson(Type type, string json)
            {
                throw new NotImplementedException();
            }

            public T FromJson<T>(TextReader reader)
            {
                throw new NotImplementedException();
            }

            public object FromJson(Type type, TextReader reader)
            {
                throw new NotImplementedException();
            }

            public string ToCleanJson(object document)
            {
                throw new NotImplementedException();
            }

            public EnumStorage EnumStorage { get; }
        }
    }
}