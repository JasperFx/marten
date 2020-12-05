using System;

namespace Marten.Schema
{
    [Obsolete("No longer used. Will be removed in version 4.")]
    public class ColumnValue
    {
        public ColumnValue(string column, string functionValue)
        {
            Column = column;
            FunctionValue = functionValue;
        }

        public string Column { get; }
        public string FunctionValue { get; }
    }
}
