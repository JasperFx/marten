namespace Marten.Schema
{
    public class ForeignKeyConstraint
    {
        public ForeignKeyConstraint(string name, string schema, string tableName)
        {
            Name = name;
            Schema = schema;
            TableName = tableName;
        }

        public string Name { get; }
        public string Schema { get; }
        public string TableName { get; }

        public override string ToString()
        {
            return $"{nameof(Name)}: {Name}, {nameof(Schema)}: {Schema}, {nameof(TableName)}: {TableName}";
        }
    }
}