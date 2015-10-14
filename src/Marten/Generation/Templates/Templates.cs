using System.Reflection;
using FubuCore;

namespace Marten.Generation.Templates
{
    public static class Templates
    {
        public static string DocumentTable()
        {
            return
                Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream(typeof (Templates), "DocumentTable.txt")
                    .ReadAllText();
        }
    }
}