namespace Marten.Linq.Results
{
    public class FirstOrDefaultHandler<T> : OnlyOneResultHandler<T>
    {
        public FirstOrDefaultHandler(DocumentQuery query) : base(1, query)
        {
        }
    }
}