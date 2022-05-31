using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Builder;
using System.Threading.Tasks;

namespace AspNetCoreWithMarten.Samples.dotnet6.ByNestedClosure
{
    #region sample_dotnet6AddMartenByNestedClosure
    //This is C# 9 style
    internal class Program
    {
        static async Task Main(string[] args) 
        {
            var builder = WebApplication.CreateBuilder(args);
            // Add services to the container.
            // See https://martendb.io/configuration/optimized_artifact_workflow.html about the OptimizeArtifactWorkflow() call.
            builder.Services.AddMarten(o => {
                o.Connection(builder.Configuration.GetConnectionString("Marten"));
            }).OptimizeArtifactWorkflow();
            builder.Services.AddControllers();
            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.MapControllers();

            await app.RunAsync();

        }
    }
    #endregion
}
