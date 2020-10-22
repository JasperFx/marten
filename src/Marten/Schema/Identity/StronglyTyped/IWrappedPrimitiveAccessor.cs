namespace Marten.Schema.Identity.StronglyTyped
{
    /// <summary>
    /// Class enables the extraction of an underlying primitive type from a strongly typed id
    /// as well as the creation of a strongly typed id from a primitive type
    /// </summary>
    /// <typeparam name="TId"></typeparam>
    /// <typeparam name="TPrimitive"></typeparam>
    public interface IWrappedPrimitiveAccessor<TId, TPrimitive>
    {
        /// <summary>
        /// Gets the primitive id value from the strongly typed id
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        TPrimitive GetId(TId obj);

        /// <summary>
        /// Creates a new strongly typed id from a primitive id value
        /// </summary>
        /// <param name="primitiveId"></param>
        /// <returns></returns>
        TId NewId(TPrimitive primitiveId);
    }
}
