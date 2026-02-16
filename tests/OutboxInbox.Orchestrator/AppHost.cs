var builder = DistributedApplication.CreateBuilder(args);

// Add PostgreSQL
var appDb = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .WithDataVolume()
    .AddDatabase("appdb");

// Add RabbitMQ
var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithDataVolume()
    .WithManagementPlugin();


// Add Combined API (Both)
builder.AddProject<Projects.OutboxInbox_Api>("api")
    .WithReference(appDb)
    .WaitFor(appDb)
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq)
    .WithExternalHttpEndpoints();

builder.Build().Run();