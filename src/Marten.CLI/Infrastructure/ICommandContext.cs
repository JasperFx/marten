using System;
using System.Collections.Generic;

namespace Marten.CLI.Infrastructure
{
    public interface ICommandContext : IDisposable
    {
        string Ask(string parameter, string description = null);
        T Single<T>();
        IEnumerable<T> All<T>();
        void Record(object result);        
    }
}