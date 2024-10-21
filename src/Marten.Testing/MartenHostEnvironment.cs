using System;
using JasperFx.Core;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Marten.Testing;

internal class MartenHostEnvironment : IHostEnvironment
{
    public string ApplicationName { get; set; } = typeof(MartenHostEnvironment).Assembly.GetName().Name;

    public IFileProvider ContentRootFileProvider { get; set; } =
        new PhysicalFileProvider(AppContext.BaseDirectory.ParentDirectory().ParentDirectory().ParentDirectory());
    public string ContentRootPath { get; set; } =
        AppContext.BaseDirectory.ParentDirectory().ParentDirectory().ParentDirectory();

    public string EnvironmentName { get; set; } = "Development";
}
