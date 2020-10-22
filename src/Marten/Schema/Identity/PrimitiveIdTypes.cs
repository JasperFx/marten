using System;

namespace Marten.Schema.Identity
{
    public static class PrimitiveIdTypes
    {
        public static readonly Type[] Supported = new[] {typeof(int), typeof(Guid), typeof(long), typeof(string)};
    }
}
