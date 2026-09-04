using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using STU.Infrastructure;
using STU.Infrastructure.Persistence;

namespace STU.PilotData;

public static class PilotDataCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var command = args.FirstOrDefault()?.Trim().ToLowerInvariant() ?? "status";
            if (command == "plan")
            {
                WriteJson(new
                {
                    PilotDatasetBlueprint.NeighborhoodCount,
                    PilotDatasetBlueprint.MicroregionCount,
                    PilotDatasetBlueprint.PropertyCount,
                    PilotDatasetBlueprint.VisitCount,
                    PilotDatasetBlueprint.UserCount,
                });
                return 0;
            }

            if (command is not ("seed" or "clean" or "status"))
            {
                Console.Error.WriteLine("Uso: STU.PilotData [plan|seed|status|clean] [--manifest caminho]");
                return 2;
            }

            var connectionString = Environment.GetEnvironmentVariable("STU_PILOT_DB_CONNECTION")
                ?? throw new InvalidOperationException("Defina STU_PILOT_DB_CONNECTION antes de acessar o ambiente piloto.");
            var target = PilotDatabaseGuard.Validate(connectionString);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:StuDatabase"] = connectionString,
                    ["Database:ApplyMigrations"] = "true",
                })
                .Build();

            var services = new ServiceCollection()
                .AddLogging()
                .AddInfrastructure(configuration)
                .AddIdentityInfrastructure()
                .BuildServiceProvider();
            await using var provider = services;

            await DatabaseInitializer.InitializeAsync(provider, configuration);
            await using var scope = provider.CreateAsyncScope();
            var service = new PilotDatasetService(
                scope.ServiceProvider.GetRequiredService<StuDbContext>(),
                scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<STU.Infrastructure.Identity.ApplicationUser>>());

            switch (command)
            {
                case "seed":
                    var password = Environment.GetEnvironmentVariable("STU_PILOT_ACCOUNT_PASSWORD")
                        ?? throw new InvalidOperationException("Defina STU_PILOT_ACCOUNT_PASSWORD antes da carga.");
                    var manifest = await service.SeedAsync(password, ReadOption(args, "--manifest"));
                    WriteJson(new { target, manifest });
                    break;
                case "clean":
                    await service.CleanAsync();
                    WriteJson(new { target, cleaned = true });
                    break;
                default:
                    WriteJson(new { target, status = await service.GetStatusAsync() });
                    break;
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static string? ReadOption(string[] args, string option)
    {
        var index = Array.FindIndex(args, item => item.Equals(option, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static void WriteJson<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}

