using System.Reflection;
using FubuCore;

namespace Marten.Generation.Templates
{
    public static class TemplateSource
    {
        public static string DocumentTable()
        {
            return
                Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream(typeof (TemplateSource), "DocumentTable.txt")
                    .ReadAllText();
        }
    }
}