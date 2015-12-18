using System;
using System.IO;

namespace Marten
{
    // SAMPLE: ISerializer
    public interface ISerializer
    {
        string ToJson(object document);
        T FromJson<T>(string json);
        T FromJson<T>(Stream stream);

        object FromJson(Type type, string json);

        /// <summary>
        /// Serialize a document without any extra
        /// type handling metadata
        /// </summary>
        /// <param name="document"></param>
        /// <returns></returns>
        string ToCleanJson(object document);
    }
    // ENDSAMPLE


}