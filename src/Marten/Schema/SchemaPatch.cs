using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Storage;

namespace Marten.Schema
{
    public class SchemaPatch
    {
        public DdlRules Rules { get; }

        public static string ToDropFileName(string updateFile)
        {
            var containingFolder = updateFile.ParentDirectory();
            var rawFileName = Path.GetFileNameWithoutExtension(updateFile);
            var ext = Path.GetExtension(updateFile);

            var dropFile = $"{rawFileName}.drop{ext}";

            return containingFolder.IsEmpty() ? dropFile : containingFolder.AppendPath(dropFile);
        }

        private readonly DDLRecorder _up = new DDLRecorder();
        private readonly DDLRecorder _down = new DDLRecorder();
        private readonly IDDLRunner _liveRunner;




        public SchemaPatch(DdlRules rules)
        {
            Rules = rules;
        }

        public SchemaPatch(DdlRules rules, StringWriter upWriter) : this(rules, new DDLRecorder(upWriter))
        {
            
        }

        public SchemaPatch(DocumentSchema schema) : this(schema.StoreOptions.DdlRules, schema)
        {
            
        }

        public SchemaPatch(DdlRules rules, IDDLRunner liveRunner) : this(rules)
        {
            _liveRunner = liveRunner;
        }

        public StringWriter UpWriter => _up.Writer;

        public IDDLRunner Updates => _liveRunner ?? _up;
        public IDDLRunner Rollbacks => _down;

        public string UpdateDDL => _up.Writer.ToString();
        public string RollbackDDL => _down.Writer.ToString();

        public SchemaPatchDifference Difference
        {
            get
            {
                if (!Migrations.Any()) return SchemaPatchDifference.None;

                if (Migrations.Any(x => x.Difference == SchemaPatchDifference.Invalid))
                {
                    return SchemaPatchDifference.Invalid;
                }

                if (Migrations.Any(x => x.Difference == SchemaPatchDifference.Update))
                {
                    return SchemaPatchDifference.Update;
                }

                if (Migrations.Any(x => x.Difference == SchemaPatchDifference.Create))
                {
                    return SchemaPatchDifference.Create;
                }

                return SchemaPatchDifference.None;
            }
        }

        public void WriteTransactionalScript(TextWriter writer, Action<TextWriter> writeStep)
        {
            writer.WriteLine("DO LANGUAGE plpgsql $tran$");
            writer.WriteLine("BEGIN");
            writer.WriteLine("");

            if (Rules.Role.IsNotEmpty())
            {
                writer.WriteLine($"SET ROLE {Rules.Role};");
                writer.WriteLine("");
            }

            writeStep(writer);

            if (Rules.Role.IsNotEmpty())
            {
                writer.WriteLine($"RESET ROLE;");
                writer.WriteLine("");
            }

            writer.WriteLine("");
            writer.WriteLine("END;");
            writer.WriteLine("$tran$;");
        }

        public void WriteTransactionalFile(string file, string sql)
        {
            using (var stream = new FileStream(file, FileMode.Create))
            {
                var writer = new StreamWriter(stream) {AutoFlush = true};

                WriteTransactionalScript(writer, w => w.WriteLine(sql));

                

                stream.Flush(true);
            }
        }

        public void WriteUpdateFile(string file)
        {
            WriteTransactionalFile(file, UpdateDDL);
        }


        public void WriteRollbackFile(string file)
        {
            WriteTransactionalFile(file, RollbackDDL);
        }

        public readonly IList<ObjectMigration> Migrations = new List<ObjectMigration>();

        public void Log(ISchemaObject schemaObject, SchemaPatchDifference difference)
        {
            var migration = new ObjectMigration(schemaObject, difference);
            Migrations.Add(migration);
        }
    }

    public class ObjectMigration
    {
        public ISchemaObject SchemaObject { get; }
        public SchemaPatchDifference Difference { get; }

        public ObjectMigration(ISchemaObject schemaObject, SchemaPatchDifference difference)
        {
            SchemaObject = schemaObject;
            Difference = difference;
        }
    }
}