using Marten.Map;
using Marten.Schema;
using Remotion.Linq.Parsing.Structure;

namespace Marten
{
    public class Session : BaseSession
    {
        public Session(IDocumentSchema schema, ISerializer serializer, ICommandRunner runner, IQueryParser parser, IMartenQueryExecutor executor) : 
            base(schema, serializer, runner, parser, executor, new NonTrackedDocumentMap(serializer))
        {
        }
    }
}