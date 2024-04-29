var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.EventPublisher>("publisher");
//builder.AddProject<Projects.CommandLineRunner>("runner");

builder.Build().Run();
