using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Linq.Includes;
using Marten.Linq.Parsing;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration
{
    internal class DocumentStatement : SelectorStatement
    {
        public DocumentStatement(IDocumentStorage storage): base(storage, storage.Fields)
        {
            Storage = storage;
        }

        public IDocumentStorage Storage { get; }

        // TODO -- return IEnumerable<ISqlFragment> instead
        protected override ISqlFragment buildWhereFragment(IMartenSession session)
        {
            if (WhereClauses.Count == 0)
                return Storage.DefaultWhereFragment();

            var parser = new WhereClauseParser(session, this);

            ISqlFragment where = null;

            switch (WhereClauses.Count)
            {
                case 0:
                    where = Storage.DefaultWhereFragment();
                    break;

                case 1:
                    where = parser.Build(WhereClauses.Single());
                    break;

                default:
                    var wheres = WhereClauses.Select(x => parser.Build(x)).ToArray();

                    where = CompoundWhereFragment.And(wheres);
                    break;


            }

            return Storage.FilterDocuments(null, where);
        }

        public override void CompileLocal(IMartenSession session)
        {
            base.CompileLocal(session);
            if (whereFragments().OfType<WhereCtIdInSubQuery>().Any())
            {
                var fragments = whereFragments().ToList();
                var subQueries = fragments.OfType<WhereCtIdInSubQuery>().ToArray();
                // TODO -- combine the sub queries here if it's the same collection!!!

                if (subQueries.Length == 1)
                {
                    // Need to set the remaining Where filters on DocumentStatement
                    Where = subQueries.CombineFragments();

                    fragments.RemoveAll(x => x is WhereCtIdInSubQuery);

                    var topLevelWhere = fragments.CombineFragments();
                    foreach (var subQuery in subQueries)
                    {
                        subQuery.Flattener.Where = topLevelWhere;
                    }
                }


            }
        }

        [Obsolete("return an enumeration instead")]
        private IEnumerable<ISqlFragment> whereFragments()
        {
            if (Where == null) yield break;

            if (Where is CompoundWhereFragment c)
            {
                foreach (var fragment in c.Children)
                {
                    yield return fragment;
                }
            }
            else
            {
                yield return Where;
            }
        }

        public override SelectorStatement UseAsEndOfTempTableAndClone(IncludeIdentitySelectorStatement includeIdentitySelectorStatement)
        {
            var clone = new DocumentStatement(Storage)
            {
                SelectClause = SelectClause,
                Orderings = Orderings,
                Mode = StatementMode.Select,
                ExportName = ExportName,
                SingleValue = SingleValue,
                CanBeMultiples = CanBeMultiples,
                ReturnDefaultWhenEmpty = ReturnDefaultWhenEmpty,
                Limit = Limit,
                Offset = Offset

            };

            // Select the Ids only
            SelectClause = includeIdentitySelectorStatement;

            // Don't do any paging here, or it'll break the Statistics
            clone.Where = new InTempTableWhereFragment(includeIdentitySelectorStatement.ExportName, "id", PagedStatement.Empty, false);

            return clone;
        }


    }
}
