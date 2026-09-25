using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using STU.Infrastructure.Persistence;

var connectionString = Environment.GetEnvironmentVariable("STU_FAMILY_RESET_CONNECTION")
    ?? throw new InvalidOperationException("Defina STU_FAMILY_RESET_CONNECTION. A conexão não será exibida.");
var connection = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false };
string? Option(string name) { var index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
var root = Path.GetFullPath(Option("--storage") ?? throw new ArgumentException("Informe --storage com o diretório operacional do ambiente."));
if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("O diretório operacional deve existir e não pode ser um link.");
await using var db = new StuDbContext(new DbContextOptionsBuilder<StuDbContext>().UseNpgsql(connection.ConnectionString, o =>
{
    o.UseNetTopologySuite();
    o.CommandTimeout(300);
}).Options);
await db.Database.OpenConnectionAsync();
if ((await db.Database.GetAppliedMigrationsAsync()).Any(m => m.EndsWith("_IndependentFamilies", StringComparison.Ordinal)))
    throw new InvalidOperationException("O novo modelo já está implantado. Este utilitário não reinicia famílias existentes.");
var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();
if (pending.Length != 1 || !pending[0].EndsWith("_IndependentFamilies", StringComparison.Ordinal))
    throw new InvalidOperationException("A base deve estar na migração imediatamente anterior a IndependentFamilies.");
string[] targets = ["properties", "property_visits", "property_versions", "property_tags", "operation_jobs"];
string[] preserved = ["health_units", "neighborhoods", "microregions", "microregion_neighborhoods", "AspNetUsers", "AspNetRoles", "AspNetRoleClaims", "operational_tags", "coverage_rules", "backup_settings", "audit_entries"];
var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
var digests = new SortedDictionary<string, string>(StringComparer.Ordinal);
foreach (var table in targets.Concat(preserved))
{
    await using var command = db.Database.GetDbConnection().CreateCommand();
    // Table identifiers come only from the fixed list above.
    command.CommandText = $"SELECT count(*), coalesce(md5(string_agg(to_jsonb(t)::text, '' ORDER BY to_jsonb(t)::text)), '') FROM \"{table}\" t";
    await using var rows = await command.ExecuteReaderAsync(); await rows.ReadAsync();
    counts[table] = rows.GetInt64(0); digests[table] = rows.GetString(1);
}
var names = new List<string>();
await using (var command = db.Database.GetDbConnection().CreateCommand())
{
    command.CommandText = "SELECT \"Id\",\"SourceFileName\",\"StagedFileName\",\"ResultFileName\" FROM operation_jobs WHERE \"Kind\" IN ('PropertyImport','PropertyExport')";
    await using var rows = await command.ExecuteReaderAsync();
    while (await rows.ReadAsync())
    {
        for (var index = 1; index <= 3; index++) if (!rows.IsDBNull(index)) names.Add(rows.GetString(index));
        // A crash may leave output before the worker records its filename in the job.
        var id = rows.GetGuid(0).ToString("N");
        foreach (var extension in new[] { ".stage.json", ".xlsx", ".csv", ".geojson", ".kml", ".gpkg" }) names.Add(id + extension);
    }
}
var files = new List<object>();
var filePaths = new List<string>();
foreach (var name in names.Distinct(StringComparer.Ordinal))
{
    if (name != Path.GetFileName(name) || name.Contains('/') || name.Contains('\\')) throw new InvalidOperationException("Referência de arquivo fora do diretório operacional.");
    var path = Path.GetFullPath(Path.Combine(root, name));
    if (!string.Equals(Path.GetDirectoryName(path), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Caminho de exclusão inválido.");
    if (!File.Exists(path)) continue;
    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Arquivo operacional não pode ser um link.");
    await using var stream = File.OpenRead(path);
    files.Add(new { name, bytes = stream.Length, sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream)) }); filePaths.Add(path);
}
var identity = new { connection.Host, connection.Port, connection.Database, connection.Username, storage = root };
var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { identity, counts, digests, files }))));
var preview = JsonSerializer.Serialize(new { identity, targets = targets.ToDictionary(t => t, t => counts[t]), preserved = preserved.ToDictionary(t => t, t => counts[t]), files, fingerprint }, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(preview);
if (!args.Contains("--apply", StringComparer.Ordinal)) return;
if (Option("--confirm") != fingerprint || Option("--database") != connection.Database || !args.Contains("--maintenance-confirmed", StringComparer.Ordinal))
    throw new InvalidOperationException("Confirme o fingerprint, o nome exato do banco e a parada do aplicativo e worker.");
var manifest = Path.GetFullPath(Option("--manifest") ?? throw new ArgumentException("Informe --manifest para registrar contagens e arquivos antes da limpeza."));
await using (var output = new FileStream(manifest, FileMode.CreateNew, FileAccess.Write, FileShare.None))
    await output.WriteAsync(Encoding.UTF8.GetBytes(preview));
// Migration owns the atomic transaction. It refuses deletion without this explicit connection-local flag.
await db.Database.ExecuteSqlRawAsync("SET stu.allow_family_reset = 'on'");
try { await db.Database.MigrateAsync(); }
finally { await db.Database.ExecuteSqlRawAsync("RESET stu.allow_family_reset"); }
foreach (var path in filePaths) File.Delete(path);
foreach (var table in preserved.Where(t => t is not ("audit_entries" or "AspNetRoleClaims")))
{
    await using var command = db.Database.GetDbConnection().CreateCommand(); command.CommandText = $"SELECT coalesce(md5(string_agg(to_jsonb(t)::text, '' ORDER BY to_jsonb(t)::text)), '') FROM \"{table}\" t";
    if ((string?)await command.ExecuteScalarAsync() != digests[table]) throw new InvalidOperationException($"Conteúdo preservado divergente em {table}. Mantenha o ambiente em manutenção.");
}
Console.WriteLine("Migração concluída, arquivos operacionais removidos e contagens preservadas conferidas. Execute os gates antes de iniciar a aplicação.");
