namespace Marten.Testing.Documents
{
    public class Cyclic
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public Node Root { get; set; }
    }


    public class Node
    {
        public Node Parent { get; set; }
        public Node Child { get; set; }
    }
}
