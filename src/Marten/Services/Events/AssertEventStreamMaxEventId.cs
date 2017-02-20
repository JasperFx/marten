using System;
using System.Text;

namespace Marten.Services.Events
{
    /// <summary>
    /// Raise exception inside transaction if expected maximum id for event stream
    /// does not match expectation
    /// </summary>
    public class AssertEventStreamMaxEventId : IStorageOperation
    {
        public Guid Stream { get; }
        public int ExpectedVersion { get; set; }
        public string TableName { get; }

        public AssertEventStreamMaxEventId(Guid stream, int expectedVersion, string tableName)
        {
            Stream = stream;
            ExpectedVersion = expectedVersion;
            TableName = tableName;
        }

        public string _sql;
        
        void ICall.WriteToSql(StringBuilder builder)
        {
            builder.Append(_sql);
        }

        public void AddParameters(IBatchCommand batch)
        {
            // Parameterized queries won't work here: https://github.com/npgsql/npgsql/issues/331
            // In order to parameterize, sproc would need to be created            
            _sql = $@"DO $$ BEGIN IF
  (SELECT max(events.version)
   FROM {TableName} AS events
   WHERE events.stream_id = '{Stream}') <> {ExpectedVersion} THEN 
RAISE EXCEPTION '{EventContracts.UnexpectedMaxEventIdForStream.Value}'; END IF; END; $$;";
        }

        public override string ToString()
        {
            return _sql;
        }

        public Type DocumentType => null;
    }
}