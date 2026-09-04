using System.Data.Common;

namespace STU.PilotData;

public static class PilotDatabaseGuard
{
    private static readonly string[] AllowedHosts = ["127.0.0.1", "localhost", "pilot-database"];

    public static PilotDatabaseTarget Validate(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        var database = Read(builder, "Database", "Initial Catalog");
        var host = Read(builder, "Host", "Server");
        var portText = ReadOptional(builder, "Port") ?? "5432";

        if (!database.EndsWith("_pilot", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A base sintética deve ter um nome terminado em '_pilot'.");
        }

        if (!AllowedHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A base sintética deve usar o host local ou o serviço pilot-database.");
        }

        if (!int.TryParse(portText, out var port) || port is < 1 or > 65535)
        {
            throw new InvalidOperationException("A porta do banco piloto é inválida.");
        }

        if (port == 55432)
        {
            throw new InvalidOperationException("A porta 55432 pertence ao banco operacional e não pode receber dados sintéticos.");
        }

        return new PilotDatabaseTarget(host, port, database);
    }

    private static string Read(DbConnectionStringBuilder builder, params string[] keys) =>
        ReadOptional(builder, keys) ?? throw new InvalidOperationException($"A conexão não informa {keys[0]}.");

    private static string? ReadOptional(DbConnectionStringBuilder builder, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (builder.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value?.ToString()))
            {
                return value.ToString()!.Trim();
            }
        }

        return null;
    }
}

public sealed record PilotDatabaseTarget(string Host, int Port, string Database);

