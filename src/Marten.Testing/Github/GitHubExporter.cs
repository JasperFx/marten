using System;
using FubuCore;
using Marten.Services;
using Octokit;

namespace Marten.Testing.Github
{
    public class GitHubExporter
    {
        private readonly string _exportPath;
        private readonly GitHubClient _client;
        private readonly IFileSystem _fileSystem = new FileSystem();
        private readonly JsonNetSerializer _serializer = new JsonNetSerializer();


        public GitHubExporter(Credentials gitHubCredentials, string exportPath)
        {
            _exportPath = exportPath;
            _client = new GitHubClient(new ProductHeaderValue("marten-testing")) {Credentials = gitHubCredentials};
        }

        public void Export(string owner, string repository)
        {
            Console.WriteLine("Starting to export {0}/{1}", owner, repository);
            var history = _client.GetHistory(owner, repository);
            var json = _serializer.ToJson(history);

            var file = repository.ToLower() + ".json";
            var path = _exportPath.AppendPath(file);

            Console.WriteLine("Writing json data to " + path);
            _fileSystem.WriteStringToFile(path, json);

        }
    }
}