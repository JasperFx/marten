namespace Marten.Schema
{
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