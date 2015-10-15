using System;

namespace Marten
{
    public interface IDocument
    {
        Guid Id { get; set; } 
    }
}