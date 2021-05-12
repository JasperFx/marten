#nullable enable
namespace Marten.Schema
{
    public enum UniqueIndexType
    {
        /// <summary>
        /// Create a duplicated field for this unique index
        /// </summary>
        DuplicatedField,

        /// <summary>
        /// Use a computed expression without duplicating the field for this unique index
        /// </summary>
        Computed
    }
}
