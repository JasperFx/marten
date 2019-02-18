using System;

namespace Marten.Linq.Parsing
{
    /// <summary>
    /// Implement !Equals for <see cref="int"/>, <see cref="long"/>, <see cref="decimal"/>, <see cref="Guid"/>, <see cref="bool"/>, <see cref="DateTime"/>, <see cref="DateTimeOffset"/>.
    /// </summary>
    /// <remarks>Equals(object) calls into <see cref="Convert.ChangeType(object, Type)"/>. Equals(null) is converted to "is null" query.</remarks>
    public sealed class SimpleNodaTimeNotEqualsParser : SimpleNodaTimeEqualsParser
    {
        public SimpleNodaTimeNotEqualsParser() : base("!=", "is not", false)
        {
        }
    }
}