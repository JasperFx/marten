namespace Marten.Transforms
{
    public interface ITransforms
    {
        void LoadFile(string file, string name = null);
        void LoadDirectory(string directory);

        void LoadJavascript(string name, string script);
    }
}