var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin();
var casinoDb = postgres.AddDatabase("casinodb");

var redis = builder.AddRedis("redis")
    .WithDataVolume();

var rabbit = builder.AddRabbitMQ("rabbitmq")
    .WithDataVolume()
    .WithManagementPlugin();

builder.AddProject<Projects.Casino_Api>("api")
    .WithReference(casinoDb).WaitFor(casinoDb)
    .WithReference(redis).WaitFor(redis)
    .WithReference(rabbit).WaitFor(rabbit)
    .WithExternalHttpEndpoints();

builder.Build().Run();
