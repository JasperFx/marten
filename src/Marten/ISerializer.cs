using System.IO;

namespace Marten
{
    public interface ISerializer
    {
        string ToJson(object document);
        T FromJson<T>(string json);
        T FromJson<T>(Stream stream);
    }
}