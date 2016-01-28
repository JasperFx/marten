namespace Marten.Schema
{
    public enum PropertySearching
    {
        /// <summary>
        /// Uses LATERAL JOINS inside the generated SQL to search immediate properties or fields
        /// </summary>
        JSONB_To_Record,
        /// <summary>
        /// Uses Postgresql's JSON locators to search within JSON data
        /// </summary>
        JSON_Locator_Only,

        /// <summary>
        /// Tries to use Postgresql's @> containment operator to search within JSON data
        /// </summary>
        ContainmentOperator
    }
}