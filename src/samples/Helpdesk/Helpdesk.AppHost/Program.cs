using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var helpdeskApi = builder.AddProject<Helpdesk_Api>("helpdesk_api");

var app = builder.Build();

app.Run();
