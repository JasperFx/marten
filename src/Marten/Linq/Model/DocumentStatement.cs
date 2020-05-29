using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Util;
using Remotion.Linq;
using Remotion.Linq.Clauses;

namespace Marten.Linq.Model
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

        public void Configure(CommandBuilder sql, int rowsLimit)
        {
            configure(sql, rowsLimit);
            if (Next != null)
            {
                sql.Append(" ");
                Next.Configure(sql, rowsLimit);
            }
        }

        protected abstract void configure(CommandBuilder command, int rowsLimit);
    }

    public class CreateTemporaryTableStatement: Statement
    {
        public CreateTemporaryTableStatement(string tableName)
        {
            ExportName = tableName;
        }

        public override string ExportName { get; }
        protected override void configure(CommandBuilder command, int rowsLimit)
        {
            command.Append("create temporary table ");
            command.Append(ExportName);
            command.Append(" as\n");
        }
    }

    public class Selector: ISelector
    {
        private readonly string _from;
        private readonly string[] _fields;

        public Selector(string from, string[] fields)
        {
            _from = @from;
            _fields = fields;
        }

        public string[] SelectFields()
        {
            return _fields;
        }

        public void WriteSelectClause(CommandBuilder sql, IQueryableDocument mapping)
        {
            sql.Append($"select {_fields.Join(", ")} from ");
            sql.Append(_from);
            sql.Append(" ");
        }
    }

    public class CommonTableExpressionStatement: Statement
    {
        private readonly IQueryableDocument _mapping;

        public CommonTableExpressionStatement(string exportName, ISelector selector, IQueryableDocument mapping)
        {
            _mapping = mapping;
            ExportName = exportName;
            Selector = selector;
        }

        public override string ExportName { get; }
        public ISelector Selector { get; }

        protected override void configure(CommandBuilder command, int rowsLimit)
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

    public class DocumentStatement : Statement
    {
        public DocumentStatement(IQueryableDocument mapping, QueryModel model)
        {
            Model = model;
            Mapping = mapping;
        }

        public IList<WhereClause> WhereClauses { get; } = new List<WhereClause>();

        private IWhereFragment _where;

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
                return Mapping.DefaultWhereFragment();

            var where = wheres.Count == 1
                ? parser.ParseWhereFragment(Mapping, wheres.Single().Predicate)
                : new CompoundWhereFragment(parser, Mapping, "and", wheres);

            return Mapping.FilterDocuments(Model, where);
        }

        public ISelector Selector { get; private set; }
        public IList<Ordering> Orderings { get; } = new List<Ordering>();

        public QueryModel Model { get; set; }

        public IQueryableDocument Mapping { get; set; }

        public int RecordLimit { get; set; } = 0;

        public void Configure(CommandBuilder sql)
        {
            Selector.WriteSelectClause(sql, Mapping);

            // TODO -- GOING TO BE UGLY, but don't write out the "From" in Selector?

            if (_where != null)
            {
                sql.Append(" where ");
                _where.Apply(sql);
            }

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

            Model?.ApplySkip(sql);
            Model?.ApplyTake(RecordLimit, sql);

        }

        private void writeOrderByFragment(CommandBuilder sql, Ordering clause)
        {
            var locator = Mapping.JsonLocator(clause.Expression);
            sql.Append(locator);

            if (clause.OrderingDirection == OrderingDirection.Desc)
            {
                sql.Append(" desc");
            }
        }

        public override string ExportName => null;
        protected override void configure(CommandBuilder command, int rowsLimit)
        {
            throw new System.NotImplementedException();
        }
    }
}
