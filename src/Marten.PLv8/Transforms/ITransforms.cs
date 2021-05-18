using System.Collections.Generic;

namespace Marten.PLv8.Transforms
{
    public interface ITransforms
    {
        void LoadFile(string file, string name = null);

        void LoadDirectory(string directory);

        void LoadJavascript(string name, string script);

        void Load(TransformFunction function);

        TransformFunction For(string name);

        IEnumerable<TransformFunction> AllFunctions();
    }
}
