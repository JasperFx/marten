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

        public override Statement UseAsEndOfTempTableAndClone(IncludeIdentitySelectorStatement includeIdentitySelectorStatement)
        {
            var clone = new DocumentStatement(_storage)
            {
                SelectClause = SelectClause,
                Orderings = Orderings,
                Mode = StatementMode.Select,
                ExportName = ExportName,

            };

            // Select the Ids only
            SelectClause = includeIdentitySelectorStatement;


            clone.Where = new InTempTableWhereFragment(includeIdentitySelectorStatement.ExportName, "id");
            Limit = Offset = 0;

            return clone;
        }


    }
}
