using System;
using System.Linq.Expressions;

namespace Marten.Transforms
{
    public interface IDocumentTransforms
    {
        void All<T>(string transformName);

        void Where<T>(string transformName, Expression<Func<T, bool>> where);

        void Document<T>(string transformName, string id);
        void Document<T>(string transformName, int id);
        void Document<T>(string transformName, long id);
        void Document<T>(string transformName, Guid id);
    }
}