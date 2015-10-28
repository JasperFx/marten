namespace Marten.Generation
{
    public class TableColumn
    {
        public string Name;
        public string Type;
        public string Directive;

        public TableColumn(string name, string type)
        {
            Name = name;
            Type = type;
        }
    }
}