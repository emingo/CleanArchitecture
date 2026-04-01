using CleanArchitecture.Shared;

var builder = DistributedApplication.CreateBuilder(args);

#if (UsePostgreSQL)
var databaseServer = builder
    .AddPostgres(Services.DatabaseServer)
    .WithLifetime(ContainerLifetime.Persistent)
    .AddDatabase(Services.Database);
#elif (UseSqlServer)
var databaseServer = builder
    .AddSqlServer(Services.DatabaseServer)
    .WithLifetime(ContainerLifetime.Persistent)
    .AddDatabase(Services.Database);
#else
var databaseServer = builder
    .AddSqlite(Services.Database);
#endif

var web = builder.AddProject<Projects.Web>(Services.WebApi)
    .WithReference(databaseServer)
    .WaitFor(databaseServer)
    .WithExternalHttpEndpoints()
    .WithAspNetCoreEnvironment()
    .WithUrlForEndpoint("http", url =>
    {
        url.DisplayText = "Scalar API Reference";
        url.Url = "/scalar";
    });

#if (!UseApiOnly)
if (builder.ExecutionContext.IsRunMode)
{
    builder.AddJavaScriptApp(Services.WebFrontend, "./../Web/ClientApp")
        .WithRunScript("start")
        .WithReference(web)
        .WaitFor(web)
        .WithHttpEndpoint(env: "PORT")
        .WithExternalHttpEndpoints();
}
#endif

builder.Build().Run();
