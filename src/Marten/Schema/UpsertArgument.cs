using System.Collections.Generic;

namespace Marten.Schema
{

    public class UpsertFunction
    {
        public readonly IList<UpsertArgument> Arguments = new List<UpsertArgument>(); 
        public readonly IList<ColumnValue> Values = new List<ColumnValue>(); 

        public string Name { get; }

        public UpsertFunction(string name)
        {
            Name = name;
        }


    }


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

    public class UpsertArgument
    {
        public string Arg { get; set; }
        public string PostgresType { get; set; }

        public string Column { get; set; }

        public string ArgumentDeclaration()
        {
            return $"{Arg} {PostgresType}";
        }

    }
}