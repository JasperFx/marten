using System;
using Baseline;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CoreTests
{
    public class TestHostEnvironment : IHostEnvironment
    {
        public string ApplicationName { get; set; } = typeof(TestHostEnvironment).Assembly.GetName().Name;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new PhysicalFileProvider(AppContext.BaseDirectory.ParentDirectory().ParentDirectory().ParentDirectory());
        public string ContentRootPath { get; set; } =
            AppContext.BaseDirectory.ParentDirectory().ParentDirectory().ParentDirectory();

        public string EnvironmentName { get; set; } = "Development";
    }
}
