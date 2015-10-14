using System.Reflection;
using FubuCore;

namespace Marten.Generation.Templates
{
    public static class TemplateSource
    {


        public static string DocumentTable()
        {
            return readStream("DocumentTable.txt");
        }

        public static string UpsertDocument()
        {
            return readStream("UpsertDocument.txt");
        }

        private static string readStream(string name)
        {
            return
                Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream(typeof (TemplateSource), name)
                    .ReadAllText();
        }
    }
}