using System.Linq;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Npgsql;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

public abstract partial class Statement: ISqlFragment
{
    public Statement Next { get; set; }
    public Statement Previous { get; set; }

    public StatementMode Mode { get; set; } = StatementMode.Select;

    /// <summary>
    ///     For CTEs
    /// </summary>
    public string ExportName { get; protected internal set; }

    public bool SingleValue { get; set; }
    public bool ReturnDefaultWhenEmpty { get; set; }
    public bool CanBeMultiples { get; set; }

    public void Apply(CommandBuilder builder)
    {
        configure(builder);
        if (Next != null)
        {
            builder.Append(" ");
            Next.Apply(builder);
        }
    }

    bool ISqlFragment.Contains(string sqlText)
    {
        return Wheres.Any(x => x.Contains(sqlText));
    }


    public void InsertAfter(Statement descendent)
    {
        if (Next != null)
        {
            Next.Previous = descendent;
            descendent.Next = Next;
        }

        Next = descendent;
        descendent.Previous = this;
    }

    /// <summary>
    ///     Place the descendent at the very end
    /// </summary>
    /// <param name="descendent"></param>
    public void AddToEnd(Statement descendent)
    {
        if (Next != null)
        {
            Next.AddToEnd(descendent);
        }
        else
        {
            Next = descendent;
            descendent.Previous = this;
        }
    }

    public void InsertBefore(Statement antecedent)
    {
        if (Previous != null)
        {
            Previous.Next = antecedent;
            antecedent.Previous = Previous;
        }

        antecedent.Next = this;
        Previous = antecedent;
    }

    public Statement Top()
    {
        return Previous == null ? this : Previous.Top();
    }

    public SelectorStatement SelectorStatement()
    {
        return (Next == null ? this : Next.SelectorStatement()).As<SelectorStatement>();
    }

    public void ConvertToCommonTableExpression(IMartenSession session)
    {
        ExportName ??= session.NextTempTableName() + "CTE";
        Mode = StatementMode.CommonTableExpression;
    }

    protected abstract void configure(CommandBuilder sql);

    protected void startCommonTableExpression(CommandBuilder sql)
    {
        if (Mode == StatementMode.CommonTableExpression)
        {
            sql.Append(Previous == null ? "WITH " : " , ");

            sql.Append(ExportName);
            sql.Append(" as (\n");
        }
    }

    protected void endCommonTableExpression(CommandBuilder sql, string suffix = null)
    {
        switch (Mode)
        {
            case StatementMode.Select:
                sql.Append(";");

                if (Next != null)
                {
                    sql.Append("\n");
                }

                return;
            case StatementMode.Inner:
                return;

            case StatementMode.CommonTableExpression:
                if (suffix.IsNotEmpty())
                {
                    sql.Append(suffix);
                }

                sql.Append("\n)\n");
                break;
        }
    }

    public NpgsqlCommand BuildCommand()
    {
        var builder = new CommandBuilder();
        Apply(builder);

        return builder.Compile();
    }
}
