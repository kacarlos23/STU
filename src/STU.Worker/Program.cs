using Microsoft.EntityFrameworkCore;
using STU.Application;
using STU.Infrastructure;
using STU.Infrastructure.Persistence;
using STU.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<OperationJobProcessor>();
builder.Services.AddScoped<BackupProcessor>();
builder.Services.AddScoped<WorkerHeartbeatReporter>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await using (var scope = host.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<StuDbContext>();
    await database.Database.MigrateAsync();
}
await host.RunAsync();
