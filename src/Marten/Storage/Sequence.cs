using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using Marten.Schema;
using Marten.Util;

namespace Marten.Storage
{
    public class Sequence : ISchemaObject
    {
        public DbObjectName Identifier { get; }

        public Sequence(DbObjectName identifier)
        {
            Identifier = identifier;
        }

        public DbObjectName Owner { get; set; }
        public string OwnerColumn { get; set; }

        public IEnumerable<DbObjectName> AllNames()
        {
            yield return Identifier;
        }

        public void Write(DdlRules rules, StringWriter writer)
        {
            writer.WriteLine($"CREATE SEQUENCE {Identifier};");

            if (Owner != null)
            {
                writer.WriteLine($"ALTER SEQUENCE {Identifier} OWNED BY {Owner}.{OwnerColumn};");
            }
            
        }

        public void WriteDropStatement(DdlRules rules, StringWriter writer)
        {
            writer.WriteLine($"DROP SEQUENCE IF EXISTS {Identifier};");
        }

        public void ConfigureQueryCommand(CommandBuilder builder)
        {
            var schemaParam = builder.AddParameter(Identifier.Schema).ParameterName;
            var nameParam = builder.AddParameter(Identifier.Name).ParameterName;
            builder.Append($"select count(*) from information_schema.sequences where sequence_schema = :{schemaParam} and sequence_name = :{nameParam};");
        }

        public SchemaPatchDifference CreatePatch(DbDataReader reader, SchemaPatch patch, AutoCreate autoCreate)
        {
            if (!reader.Read() || reader.GetInt32(0) == 0)
            {
                Write(patch.Rules, patch.UpWriter);
                return SchemaPatchDifference.Create;
            }

            return SchemaPatchDifference.None;
        }
    }
}