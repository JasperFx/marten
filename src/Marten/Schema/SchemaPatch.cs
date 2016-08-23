using System;
using System.IO;
using Baseline;

namespace Marten.Schema
{
    public class SchemaPatch
    {
        public DdlRules Rules { get; set; }

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

        public static void WriteTransactionalScript(TextWriter writer, Action<TextWriter> writeStep)
        {
            writer.WriteLine("DO LANGUAGE plpgsql $tran$");
            writer.WriteLine("BEGIN");
            writer.WriteLine("");



            writeStep(writer);

            writer.WriteLine("");
            writer.WriteLine("END;");
            writer.WriteLine("$tran$;");
        }

        public static void WriteTransactionalFile(string file, string sql)
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
    }
}