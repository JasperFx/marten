using System.Linq;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Linq.Filters;
using Marten.Linq.Includes;
using Marten.Linq.Parsing;

namespace Marten.Linq.SqlGeneration
{
    public class DocumentStatement : SelectorStatement
    {
        private readonly IDocumentStorage _storage;

        public DocumentStatement(IDocumentStorage storage): base(storage, storage.Fields)
        {
            _storage = storage;
        }

        protected override ISqlFragment buildWhereFragment(IMartenSession session)
        {
            if (WhereClauses.Count == 0)
                return _storage.DefaultWhereFragment();

            var parser = new WhereClauseParser(session, this);

            ISqlFragment where = null;

            switch (WhereClauses.Count)
            {
                case 0:
                    where = _storage.DefaultWhereFragment();
                    break;

                case 1:
                    where = parser.Build(WhereClauses.Single());
                    break;

                default:
                    var wheres = WhereClauses.Select(x => parser.Build(x)).ToArray();

                    where = new CompoundWhereFragment("and", wheres);
                    break;


            }

            return _storage.FilterDocuments(null, where);
        }

        public override SelectorStatement UseAsEndOfTempTableAndClone(IncludeIdentitySelectorStatement includeIdentitySelectorStatement)
        {
            var clone = new DocumentStatement(_storage)
            {
                SelectClause = SelectClause,
                Orderings = Orderings,
                Mode = StatementMode.Select,
                ExportName = ExportName,
                SingleValue = SingleValue,
                CanBeMultiples = CanBeMultiples,
                ReturnDefaultWhenEmpty = ReturnDefaultWhenEmpty

            };

            // Select the Ids only
            SelectClause = includeIdentitySelectorStatement;

            clone.Where = new InTempTableWhereFragment(includeIdentitySelectorStatement.ExportName, "id");
            Limit = Offset = 0;

            return clone;
        }


    }
}
