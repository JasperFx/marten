using System;
using System.IO;

namespace Marten
{
    public interface ISerializer
    {
        string ToJson(object document);
        T FromJson<T>(string json);
        T FromJson<T>(Stream stream);
        object FromJson(Type type, string json);

        string ToCleanJson(object document);
    }
}