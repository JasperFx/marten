using System;

namespace Marten.Services
{
    public interface IExceptionTransform
    {
        bool TryTransform(Exception original, out Exception transformed);
    }
}