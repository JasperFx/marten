using Marten.Linq;
using Marten.Schema;
using Marten.Services;
using Remotion.Linq.Parsing.Structure;

namespace Marten
{
    public class Session : BaseSession
    {
        public Session(IDocumentSchema schema, ISerializer serializer, ICommandRunner runner, IQueryParser parser, IMartenQueryExecutor executor, IDiagnostics diagnostics) : 
            base(schema, serializer, runner, parser, executor, new NulloIdentityMap(serializer), diagnostics)
        {
        }
    }
}