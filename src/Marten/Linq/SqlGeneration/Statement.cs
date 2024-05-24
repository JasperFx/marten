using System;
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

    public void Apply(ICommandBuilder builder)
    {
        configure(builder);
        if (Next != null)
        {
            if (Mode == StatementMode.Select)
            {
                builder.StartNewCommand();
            }

            builder.Append(" ");
            Next.Apply(builder);
        }
    }

    public void InsertAfter(Statement descendent)
    {
        if (Next != null)
        {
            Next.Previous = descendent;
            descendent.Next = Next;
        }

        if (object.ReferenceEquals(this, descendent))
        {
            throw new InvalidOperationException(
                "Whoa pardner, you cannot set Next to yourself, that's a stack overflow!");
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
            if (object.ReferenceEquals(this, descendent))
            {
                return;
            }

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

    protected abstract void configure(ICommandBuilder sql);

    protected void startCommonTableExpression(ICommandBuilder sql)
    {
        if (Mode == StatementMode.CommonTableExpression)
        {
            sql.Append(Previous == null ? "WITH " : " , ");

            sql.Append(ExportName);
            sql.Append(" as (");
        }
    }

    protected void endCommonTableExpression(ICommandBuilder sql, string suffix = null)
    {
        switch (Mode)
        {
            case StatementMode.Select:
                sql.Append(";");

                return;
            case StatementMode.Inner:
                return;

            case StatementMode.CommonTableExpression:
                if (suffix.IsNotEmpty())
                {
                    sql.Append(suffix);
                }

                sql.Append(')');
                break;
        }
    }

    public NpgsqlCommand BuildCommand(IMartenSession session)
    {
        var builder = new CommandBuilder(){TenantId = session.TenantId};
        Apply(builder);

        return builder.Compile();
    }
}
