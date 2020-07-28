using System;
using Marten.Util;

namespace Marten.Internal.Linq
{
    public class CountStatement<T> : Statement where T : struct
    {
        public CountStatement(Statement parent) : base(new ScalarSelectClause<T>("count(*)", parent.ExportName), null)
        {
            if (parent.Mode != StatementMode.CommonTableExpression) throw new ArgumentOutOfRangeException(nameof(parent), "CountStatement's parent must be a Common Table Expression statement");
            parent.Next = this;

            TableName = parent.ExportName;

            SingleValue = true;
            ReturnDefaultWhenEmpty = true;
            CanBeMultiples = true;
        }

        public string TableName { get; }

        protected override void configure(CommandBuilder sql)
        {
            sql.Append("select count(*) from ");
            sql.Append(TableName);
        }
    }
}
