using System.Collections.Generic;

namespace Marten.Services
{
    public interface IDocumentTracker
    {
        IEnumerable<DocumentChange> DetectChanges();
    }
}