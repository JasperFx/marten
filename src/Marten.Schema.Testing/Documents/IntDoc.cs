namespace Marten.Schema.Testing.Documents
{
    public class IntDoc
    {
        public int Id { get; set; }

        public IntDoc()
        {
        }

        public IntDoc(int id)
        {
            Id = id;
        }
    }

    public class LongDoc
    {
        public long Id { get; set; }
    }
}
