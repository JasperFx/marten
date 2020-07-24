using System;
using System.Linq;
using Baseline;
using Marten.Internal.Linq.Includes;
using Marten.Internal.Storage;
using Marten.Linq;

namespace Marten.Internal.Linq
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
            // TODO -- this logic is duplicated. Pull it into a helper somewhere.
            // Find the duplication of DefaultWhereFragment/ FilterDocuments in other places
            if (WhereClauses.Count == 0)
                return _storage.DefaultWhereFragment();

            var where = WhereClauses.Count == 1
                ? parser.ParseWhereFragment(Fields, WhereClauses.Single().Predicate)
                : new CompoundWhereFragment(parser, Fields, "and", WhereClauses);

            return _storage.FilterDocuments(null, where);
        }

        public Statement CloneForTempTableCreation(IncludeIdentitySelectorStatement includes)
        {
            // This is for a first level query w/ no SelectManys
            if (Mode == StatementMode.Select)
            {
                var clone = new DocumentStatement(_storage)
                {
                    Offset = Offset,
                    Limit = Limit,
                    Where = Where,
                    SelectClause = includes,
                    Orderings = Orderings,
                    Next = Next,
                    Mode = Mode,
                    ExportName = ExportName
                };

                clone.WhereClauses.AddRange(WhereClauses);

                return clone;
            }

            throw new NotSupportedException("Not yet");

        }
    }
}
