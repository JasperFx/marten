using System.Linq;
using Marten.Linq;

namespace Marten.V4Internals.Linq
{
    public class DocumentStatement : Statement
    {
        private readonly IDocumentStorage _storage;

        public DocumentStatement(IDocumentStorage storage): base(storage, storage.Fields)
        {
            _storage = storage;
        }

        protected override IWhereFragment buildWhereFragment(MartenExpressionParser parser)
        {
            if (WhereClauses.Count == 0)
                return _storage.DefaultWhereFragment();

            var where = WhereClauses.Count == 1
                ? parser.ParseWhereFragment(Fields, WhereClauses.Single().Predicate)
                : new CompoundWhereFragment(parser, Fields, "and", WhereClauses);

            return _storage.FilterDocuments(null, where);
        }
    }
}
