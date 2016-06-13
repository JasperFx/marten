using System.IO;

namespace Marten.Schema
{
    public class SchemaPatch
    {
        private readonly DDLRecorder _up = new DDLRecorder();
        private readonly DDLRecorder _down = new DDLRecorder();
        private readonly IDDLRunner _liveRunner;

        public SchemaPatch()
        {
        }

        public SchemaPatch(IDDLRunner liveRunner)
        {
            _liveRunner = liveRunner;
        }

        public StringWriter UpWriter => _up.Writer;

        public IDDLRunner Updates => _liveRunner ?? _up;
        public IDDLRunner DownRunner => _down;

        public string UpdateDDL => _up.Writer.ToString();
        public string RollbackDDL => _down.Writer.ToString();
    }
}