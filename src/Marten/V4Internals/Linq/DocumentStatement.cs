using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Linq;
using Marten.Linq.Fields;
using Marten.Schema;
using Marten.Util;
using Remotion.Linq;
using Remotion.Linq.Clauses;

namespace Marten.V4Internals.Linq
{
    // Types?
    // SelectDocumentStatement -- the main selector
    // IncludeDocumentStatement
    public abstract class Statement
    {
        public Statement Previous { get; protected set; }
        public Statement Next { get; protected set; }

        /// <summary>
        /// For CTEs
        /// </summary>
        public abstract string ExportName { get; }

        public void Configure(CommandBuilder sql, int rowsLimit, bool withStatistics)
        {
            configure(sql, rowsLimit, withStatistics);
            if (Next != null)
            {
                sql.Append(" ");
                Next.Configure(sql, rowsLimit, withStatistics);
            }
        }

        public V4Internals.ISelector Selector { get; protected set; }
        public IList<Ordering> Orderings { get; } = new List<Ordering>();
        public IFieldMapping Mapping { get; protected set; }

        protected abstract void configure(CommandBuilder command, int rowsLimit, bool withStatistics);

        protected void writeOrderByFragment(CommandBuilder sql, Ordering clause)
        {
            var locator = Mapping.FieldFor(clause.Expression).TypedLocator;
            sql.Append(locator);

            if (clause.OrderingDirection == OrderingDirection.Desc)
            {
                sql.Append(" desc");
            }
        }

        protected void writeOrderClause(CommandBuilder sql)
        {
            if (Orderings.Any())
            {
                sql.Append(" order by ");
                writeOrderByFragment(sql, Orderings[0]);
                for (var i = 1; i < Orderings.Count; i++)
                {
                    sql.Append(", ");
                    writeOrderByFragment(sql, Orderings[i]);
                }
            }
        }
    }

    public class CreateTemporaryTableStatement: Statement
    {
        public CreateTemporaryTableStatement(string tableName)
        {
            ExportName = tableName;
        }

        public override string ExportName { get; }
        protected override void configure(CommandBuilder command, int rowsLimit, bool withStatistics)
        {
            command.Append("create temporary table ");
            command.Append(ExportName);
            command.Append(" as\n");
        }
    }


    public class CommonTableExpressionStatement: Statement
    {
        private readonly IQueryableDocument _mapping;

        public CommonTableExpressionStatement(string exportName, Marten.Linq.ISelector selector, IQueryableDocument mapping)
        {
            _mapping = mapping;
            ExportName = exportName;
            Selector = selector;
        }

        public override string ExportName { get; }
        public Marten.Linq.ISelector Selector { get; }

        protected override void configure(CommandBuilder command, int rowsLimit, bool withStatistics)
        {
            command.Append(Previous == null ? "WITH " : " , ");

            command.Append(ExportName);
            command.Append(" as (");

            Selector.WriteSelectClause(command, _mapping);



// WHAT HERE??????

            // TODO stuff here
            command.Append("\n)");
        }
    }


    // TODO -- SelectStatement is going to have to understand if it's
    // inside a CTE or last. Combine CTE and SelectStatement!

    public class DocumentStatement<T> : Statement
    {
        public DocumentStatement(V4Internals.IDocumentStorage<T> storage, QueryModel model)
        {
            Model = model;
            Mapping = storage.Fields;
            _storage = storage;

            throw new NotImplementedException();
            //Selector = storage;
        }

        public IList<WhereClause> WhereClauses { get; } = new List<WhereClause>();

        private IWhereFragment _where;
        private V4Internals.IDocumentStorage<T> _storage;

        // TODO -- this is going to set up CTEs later for sub query
        // usage
        public void CompileStructure(MartenExpressionParser parser)
        {
            _where = buildWhereFragment(parser);
        }

        private IWhereFragment buildWhereFragment(MartenExpressionParser parser)
        {
            var wheres = WhereClauses;
            if (wheres.Count == 0)
                return _storage.DefaultWhereFragment();

            var where = wheres.Count == 1
                ? parser.ParseWhereFragment(Mapping, wheres.Single().Predicate)
                : new CompoundWhereFragment(parser, Mapping, "and", wheres);

            return _storage.FilterDocuments(Model, where);
        }

        public QueryModel Model { get; set; }

        public override string ExportName => null;
        protected override void configure(CommandBuilder sql, int rowsLimit, bool withStatistics)
        {
            Selector.WriteSelectClause(sql, withStatistics);

            // TODO -- GOING TO BE UGLY, but don't write out the "From" in Selector?

            if (_where != null)
            {
                sql.Append(" where ");
                _where.Apply(sql);
            }

            writeOrderClause(sql);

            Model?.ApplySkip(sql);
            Model?.ApplyTake(rowsLimit, sql);
        }
    }
}
