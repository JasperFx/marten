using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Internal;
using Marten.Linq.Fields;
using Marten.Linq.Filters;
using Marten.Linq.Parsing;
using Weasel.Postgresql;
using Marten.Util;
using Npgsql;
using Remotion.Linq.Clauses;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration
{
    /// <summary>
    /// Internal model used to generate SQL within Linq queries
    /// </summary>
    public abstract class Statement
    {
        private Statement _next;

        protected Statement(IFieldMapping fields)
        {
            Fields = fields;
        }

        public string FromObject { get; protected set; }

        public Statement Previous { get; private set; }

        public Statement Next
        {
            get => _next;
            private set
            {
                _next = value;
                if (value != null) value.Previous = this;
            }
        }

        public StatementMode Mode { get; set; } = StatementMode.Select;

        /// <summary>
        ///     For CTEs
        /// </summary>
        public string ExportName { get; protected set; }


        public IList<Ordering> Orderings { get; protected set; } = new List<Ordering>();
        public IFieldMapping Fields { get; }

        public IList<WhereClause> WhereClauses { get; } = new List<WhereClause>();

        public int Offset { get; set; }
        public int Limit { get; set; }

        protected virtual bool IsSubQuery => false;

        public ISqlFragment Where { get; internal set; }

        public bool SingleValue { get; set; }
        public bool ReturnDefaultWhenEmpty { get; set; }
        public bool CanBeMultiples { get; set; }

        public Statement Top()
        {
            return Previous == null ? this : Previous.Top();
        }

        public Statement Current()
        {
            return Next == null ? this : Next.Current();
        }

        public void Configure(CommandBuilder sql)
        {
            configure(sql);
            if (Next != null)
            {
                sql.Append(" ");
                Next.Configure(sql);
            }
        }

        protected abstract void configure(CommandBuilder builder);


        protected void writeWhereClause(CommandBuilder sql)
        {
            if (Where != null)
            {
                sql.Append(" where ");
                Where.Apply(sql);
            }
        }

        protected void writeOrderByFragment(CommandBuilder sql, Ordering clause)
        {
            var field = Fields.FieldFor(clause.Expression);
            var locator = field.ToOrderExpression(clause.Expression);
            sql.Append(locator);

            if (clause.OrderingDirection == OrderingDirection.Desc) sql.Append(" desc");
        }

        protected virtual ISqlFragment buildWhereFragment(IMartenSession session)
        {
            if (!WhereClauses.Any()) return null;

            var parser = new WhereClauseParser(session, this) {InSubQuery = IsSubQuery};

            if (WhereClauses.Count == 1) return parser.Build(WhereClauses.Single());

            var wheres = WhereClauses.Select(x => parser.Build(x)).ToArray();
            return CompoundWhereFragment.And(wheres);
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

        public void CompileStructure(IMartenSession session)
        {
            CompileLocal(session);
            Next?.CompileStructure(session);
        }


        public virtual void CompileLocal(IMartenSession session)
        {
            // Where clauses are pre-built in the case of includes
            Where ??= buildWhereFragment(session);
        }


        public void ConvertToCommonTableExpression(IMartenSession session)
        {
            ExportName ??= session.NextTempTableName() + "CTE";
            Mode = StatementMode.CommonTableExpression;
        }

        public void InsertBefore(Statement antecedent)
        {
            if (Previous != null) Previous.Next = antecedent;

            antecedent.Next = this;
        }

        public void InsertAfter(Statement descendent)
        {
            if (Next != null) descendent.Next = Next;

            Next = descendent;
        }

        protected void startCommonTableExpression(CommandBuilder sql)
        {
            if (Mode == StatementMode.Select) return;

            sql.Append(Previous == null ? "WITH " : " , ");

            sql.Append(ExportName);
            sql.Append(" as (\n");
        }

        protected void endCommonTableExpression(CommandBuilder sql, string suffix = null)
        {
            if (Mode == StatementMode.Select) return;

            if (suffix.IsNotEmpty()) sql.Append(suffix);

            sql.Append("\n)\n");
        }

        public NpgsqlCommand BuildCommand()
        {
            var builder = new CommandBuilder();
            Configure(builder);

            return builder.Compile();
        }
    }
}
