using System;

namespace Marten.Services
{
    // If it's an ICallback, register itself as the ICallback
    public interface IStorageOperation : ICall
    {
        void AddParameters(IBatchCommand batch);

        Type DocumentType { get; }
    }
}