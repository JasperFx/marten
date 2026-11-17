var builder = DistributedApplication.CreateBuilder(args);

// Aspire owns the PostgreSQL server. Both services get its connection string as ConnectionStrings:marten,
// and do not start until the database is accepting connections.
var marten = builder.AddPostgres("postgres").AddDatabase("marten");

builder.AddProject<Projects.EventPublisher>("publisher")
    .WithReference(marten)
    .WaitFor(marten);

builder.AddProject<Projects.AspireHeadlessTripService>("runner")
    .WithReference(marten)
    .WaitFor(marten);

builder.Build().Run();
