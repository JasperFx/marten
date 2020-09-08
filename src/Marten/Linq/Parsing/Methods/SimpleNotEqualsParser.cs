using System;

namespace Marten.Linq.Parsing.Methods
{
    /// <summary>
    /// Implement !Equals for <see cref="int"/>, <see cref="long"/>, <see cref="decimal"/>, <see cref="Guid"/>, <see cref="bool"/>, <see cref="DateTime"/>, <see cref="DateTimeOffset"/>.
    /// </summary>
    /// <remarks>Equals(object) calls into <see cref="Convert.ChangeType(object, Type)"/>. Equals(null) is converted to "is null" query.</remarks>
    internal sealed class SimpleNotEqualsParser: SimpleEqualsParser
    {
        public SimpleNotEqualsParser() : base("!=", "is not", false)
        {
        }
    }
}
