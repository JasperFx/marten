var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.EventPublisher>("publisher");
builder.AddProject<Projects.AspireHeadlessTripService>("runner");

builder.Build().Run();
