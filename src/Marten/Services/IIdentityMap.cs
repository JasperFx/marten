using System;
using Marten.Schema;

namespace Marten.Services
{
    public interface IIdentityMap
    {
        T Get<T>(object id, Func<string> json);
        T Get<T>(object id, string json);
    }
}