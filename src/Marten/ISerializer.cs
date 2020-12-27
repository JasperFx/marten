using System;
using System.IO;
using System.Threading.Tasks;

namespace Marten
{
    // SAMPLE: ISerializer
    public interface ISerializer
    {
        /// <summary>
        /// Serialize the document object into <paramref name="stream"/>.
        /// </summary>
        void ToJson(object document, Stream stream);

        /// <summary>
        /// Serialize the document object into a JSON string
        /// </summary>
        /// <param name="document"></param>
        /// <returns></returns>
        string ToJson(object document);

        /// <summary>
        /// Deserialize a JSON string into an object of type T
        /// </summary>
        T FromJson<T>(Stream stream);


        /// <summary>
        /// Deserialize a JSON string into an object of type T
        /// </summary>
        Task<T> FromJsonAsync<T>(Stream stream);

        /// <summary>
        /// Deserialize a JSON string into the supplied Type
        /// </summary>
        object FromJson(Type type, Stream stream);


        /// <summary>
        /// Deserialize a JSON string into the supplied Type
        /// </summary>
        Task<object> FromJsonAsync(Type type, Stream stream);

        /// <summary>
        /// Serialize a document without any extra
        /// type handling metadata
        /// </summary>
        /// <param name="document"></param>
        /// <returns></returns>
        string ToCleanJson(object document);

        /// <summary>
        /// Just gotta tell Marten if enum's are stored
        /// as int's or string's in the JSON
        /// </summary>
        EnumStorage EnumStorage { get; }

        /// <summary>
        /// Specify whether properties in the JSON document should use Camel or Pascal casing.
        /// </summary>
        Casing Casing { get; }

        /// <summary>
        /// Write the JSON for a document with embedded
        /// type information. This is used inside the patching API
        /// to handle polymorphic collections
        /// </summary>
        /// <param name="document"></param>
        /// <returns></returns>
        string ToJsonWithTypes(object document);
    }

    // ENDSAMPLE

    /// <summary>
    /// Governs how .Net Enum types are persisted
    /// in the serialized JSON
    /// </summary>
    public enum EnumStorage
    {
        /// <summary>
        /// Serialize Enum values as their integer value
        /// </summary>
        AsInteger,

        /// <summary>
        /// Serialize Enum values as their string value
        /// </summary>
        AsString
    }

    /// <summary>
    /// Governs the JSON serialization behavior of how .Net
    /// member names are persisted in the JSON stored in
    /// the database
    /// </summary>
    public enum Casing
    {
        /// <summary>
        /// Exactly mimic the .Net member names in the JSON persisted to the database
        /// </summary>
        Default,

        /// <summary>
        /// Force the .Net member names to camel casing when serialized to JSON in
        /// the database
        /// </summary>
        CamelCase,

        /// <summary>
        /// Force the .Net member names to snake casing when serialized to JSON in
        /// the database
        /// </summary>
        SnakeCase
    }

    /// <summary>
    /// Governs .Net collection serialization
    /// </summary>
    public enum CollectionStorage
    {
        Default,
        AsArray
    }

    [Flags]
    public enum NonPublicMembersStorage
    {
        Default = 0,
        NonPublicSetters = 1,
        NonPublicDefaultConstructor = 2
    }
}
