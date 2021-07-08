using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Weasel.Core;
using Weasel.Postgresql;

#nullable enable
namespace Marten
{


    #region sample_ISerializer

    /// <summary>
    /// When selecting data through Linq Select() transforms,
    /// should the data elements returned from Postgresql be
    /// cast to their raw types or simple strings
    /// </summary>
    public enum ValueCasting
    {
        /// <summary>
        /// Json fields will be returned with their values cast to
        /// the proper type. I.e., {"number": 1}
        /// </summary>
        Strict,

        /// <summary>
        /// Json fields will be returned with their values in simple
        /// string values. I.e., {"number": "1"}
        /// </summary>
        Relaxed
    }

    public interface ISerializer
    {
        /// <summary>
        /// Serialize the document object into a JSON string
        /// </summary>
        /// <param name="document"></param>
        /// <returns></returns>
        string ToJson(object document);

        /// <summary>
        /// Deserialize a JSON string stream into an object of type T
        /// </summary>
        T FromJson<T>(Stream stream);

        /// <summary>
        /// Deserialize a JSON string into an object of type T
        /// </summary>
        T FromJson<T>(DbDataReader reader, int index);

        /// <summary>
        /// Deserialize a JSON string stream into an object of type T
        /// </summary>
        ValueTask<T> FromJsonAsync<T>(Stream stream, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deserialize a JSON string into an object of type T
        /// </summary>
        ValueTask<T> FromJsonAsync<T>(DbDataReader reader, int index, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deserialize a JSON string stream into an object of type T
        /// </summary>
        object FromJson(Type type, Stream stream);

        /// <summary>
        /// Deserialize a JSON string into the supplied Type
        /// </summary>
        object FromJson(Type type, DbDataReader reader, int index);

        /// <summary>
        /// Deserialize a JSON string stream into an object of type T
        /// </summary>
        ValueTask<object> FromJsonAsync(Type type, Stream stream, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deserialize a JSON string into the supplied Type
        /// </summary>
        ValueTask<object> FromJsonAsync(Type type, DbDataReader reader, int index, CancellationToken cancellationToken = default);

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

        /// <summary>
        /// Controls how the Linq Select() behavior needs to work in the database
        /// </summary>
        ValueCasting ValueCasting { get; }
    }

    #endregion sample_ISerializer

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
        /// <summary>
        /// Use default serialization for collections according to the serializer
        /// being used
        /// </summary>
        Default,

        /// <summary>
        /// Direct the underlying serializer to serialize collections as JSON arrays
        /// </summary>
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
