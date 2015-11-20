
using Marten.Map;
using Marten.Schema;
using Remotion.Linq.Parsing.Structure;

namespace Marten
{
    public class TrackingSession : BaseSession, ITrackingSession
    {
        public TrackingSession(IDocumentSchema schema, ISerializer serializer, ICommandRunner runner, IQueryParser parser, IMartenQueryExecutor executor, IDiagnostics diagnostics) : 
            base(schema, serializer, runner, parser, executor, new TrackedDocumentMap(serializer), diagnostics)
        {
        }
    }
}