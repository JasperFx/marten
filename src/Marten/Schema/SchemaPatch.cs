using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Storage;
using Marten.Util;
using Npgsql;

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

        public SchemaPatch(DdlRules rules, IDDLRunner liveRunner) : this(rules)
        {
            _liveRunner = liveRunner;
        }

        public StringWriter DownWriter => _down.Writer;

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

        public void WriteScript(TextWriter writer, Action<TextWriter> writeStep, bool transactionalScript=true)
        {
            if (transactionalScript)
            {
                writer.WriteLine("DO LANGUAGE plpgsql $tran$");
                writer.WriteLine("BEGIN");
                writer.WriteLine("");
            }

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

            if (transactionalScript)
            {
                writer.WriteLine("");
                writer.WriteLine("END;");
                writer.WriteLine("$tran$;");
            }
        }

        public void WriteFile(string file, string sql, bool transactionalScript)
        {
            using (var stream = new FileStream(file, FileMode.Create))
            {
                var writer = new StreamWriter(stream) {AutoFlush = true};

                WriteScript(writer, w => w.WriteLine(sql), transactionalScript);
                
                stream.Flush(true);
            }
        }

        public void WriteUpdateFile(string file, bool transactionalScript = true)
        {
            WriteFile(file, UpdateDDL, transactionalScript);
        }


        public void WriteRollbackFile(string file, bool transactionalScript = true)
        {
            WriteFile(file, RollbackDDL, transactionalScript);
        }

        public readonly IList<ObjectMigration> Migrations = new List<ObjectMigration>();

        public void Log(ISchemaObject schemaObject, SchemaPatchDifference difference)
        {
            var migration = new ObjectMigration(schemaObject, difference);
            Migrations.Add(migration);
        }

        public void AssertPatchingIsValid(AutoCreate autoCreate)
        {
            if (autoCreate == AutoCreate.All) return;

            var difference = Difference;

            if (difference == SchemaPatchDifference.None) return;
            

            if (difference == SchemaPatchDifference.Invalid)
            {
                var invalidObjects = Migrations.Where(x => x.Difference == SchemaPatchDifference.Invalid).Select(x => x.SchemaObject.Identifier.ToString()).Join(", ");
                throw new InvalidOperationException($"Marten cannot derive updates for objects {invalidObjects}");
            }

            if (difference == SchemaPatchDifference.Update && autoCreate == AutoCreate.CreateOnly)
            {
                var updates = Migrations.Where(x => x.Difference == SchemaPatchDifference.Update).ToArray();
                if (updates.Any())
                {
                    throw new InvalidOperationException($"Marten cannot apply updates in CreateOnly mode to existing items {updates.Select(x => x.SchemaObject.Identifier.QualifiedName).Join(", ")}");
                }
            }
        }

        
        public void Apply(NpgsqlConnection conn, AutoCreate autoCreate, ISchemaObject[] schemaObjects)
        {
            if (!schemaObjects.Any()) return;

            // Let folks just fail if anything is wrong.
            // Per https://github.com/JasperFx/marten/issues/711
            if (autoCreate == AutoCreate.None) return;

            var cmd = conn.CreateCommand();
            var builder = new CommandBuilder(cmd);

            foreach (var schemaObject in schemaObjects)
            {
                schemaObject.ConfigureQueryCommand(builder);
            }

            cmd.CommandText = builder.ToString();

            try
            {
                using (var reader = cmd.ExecuteReader())
                {
                    apply(schemaObjects[0], autoCreate, reader);
                    for (int i = 1; i < schemaObjects.Length; i++)
                    {
                        reader.NextResult();
                        apply(schemaObjects[i], autoCreate, reader);
                    }
                }
            }
            catch (Exception e)
            {
                throw new MartenCommandException(cmd, e);
            }

            AssertPatchingIsValid(autoCreate);
        }

        private void apply(ISchemaObject schemaObject, AutoCreate autoCreate, DbDataReader reader)
        {
            var difference = schemaObject.CreatePatch(reader, this, autoCreate);
            Migrations.Add(new ObjectMigration(schemaObject, difference));
        }

        public void Apply(NpgsqlConnection connection, AutoCreate autoCreate, ISchemaObject schemaObject)
        {
            Apply(connection, autoCreate, new ISchemaObject[] {schemaObject});
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