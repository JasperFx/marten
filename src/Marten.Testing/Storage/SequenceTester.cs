using System;
using System.IO;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Harness;
using Marten.Util;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.Testing.Storage
{
    public class SequenceTester : IDisposable
    {
        private readonly NpgsqlConnection _conn;
        private readonly Sequence theSequence = new Sequence(new DbObjectName("testbed", "mysequence"));

        public SequenceTester()
        {
            _conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
            _conn.Open();

            _conn.CreateCommand("drop schema if exists testbed cascade;create schema testbed")
                .ExecuteNonQuery();
        }

        public void Dispose()
        {
            _conn.Close();
            _conn.Dispose();
        }

        [Fact]
        public void can_create_sequence_without_blowing_up()
        {
            var writer = new StringWriter();
            theSequence.Write(new DdlRules(), writer);

            _conn.CreateCommand(writer.ToString()).ExecuteNonQuery();
        }


        [Fact]
        public void determine_that_it_is_missing()
        {
            var patch = new SchemaPatch(new DdlRules());
            patch.Apply(_conn, AutoCreate.All, theSequence);

            patch.Difference.ShouldBe(SchemaPatchDifference.Create);
        }

        [Fact]
        public void determine_that_it_is_already_there()
        {
            can_create_sequence_without_blowing_up();

            var patch = new SchemaPatch(new DdlRules());
            patch.Apply(_conn, AutoCreate.All, theSequence);

            patch.Difference.ShouldBe(SchemaPatchDifference.None);
        }
    }
}
