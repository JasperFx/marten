using System;
using System.IO;

namespace Marten
{
    // SAMPLE: ISerializer
    public interface ISerializer
    {
        /// <summary>
        /// Serialize the document object into <paramref name="writer"/>.
        /// </summary>
        /// <param name="document"></param>
        /// <param name="writer"></param>
        void ToJson(object document, TextWriter writer);

        /// <summary>
        /// Serialize the document object into a JSON string
        /// </summary>
        /// <param name="document"></param>
        /// <returns></returns>
        string ToJson(object document);

        /// <summary>
        /// Deserialize a JSON string into an object of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="reader"></param>
        /// <returns></returns>
        T FromJson<T>(TextReader reader);

        /// <summary>
        /// Deserialize a JSON string into the supplied Type
        /// </summary>
        /// <param name="type"></param>
        /// <param name="reader"></param>
        /// <returns></returns>
        object FromJson(Type type, TextReader reader);

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
        /// Specify whether collections should be stored as json arrays (without type names)
        /// </summary>
        CollectionStorage CollectionStorage { get; }

        /// <summary>
        /// Specify whether non public members should be used during deserialization
        /// </summary>
        NonPublicMembersStorage NonPublicMembersStorage { get; }

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

    public enum EnumStorage
    {
        AsInteger,
        AsString
    }

    public enum Casing
    {
        Default,
        CamelCase,
        SnakeCase
    }

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
