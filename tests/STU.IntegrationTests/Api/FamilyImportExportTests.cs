extern alias Worker;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using STU.Domain.Operations;
using STU.Infrastructure.Persistence;
using OperationJobProcessor = Worker::STU.Worker.OperationJobProcessor;

namespace STU.IntegrationTests.Api;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "xUnit IAsyncLifetime disposes the factory and its container.")]
public sealed class FamilyImportExportTests : IAsyncLifetime
{
    private readonly StuApiFactory factory = new();
    private readonly string storage = Path.Combine(Path.GetTempPath(), "stu-families-test-" + Guid.NewGuid().ToString("N"));
    public Task InitializeAsync() { Directory.CreateDirectory(storage); return factory.InitializeAsync(); }
    public async Task DisposeAsync()
    {
        await factory.DisposeAsync(); await ((IAsyncLifetime)factory).DisposeAsync();
        if (Path.GetDirectoryName(Path.GetFullPath(storage)) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) && Path.GetFileName(storage).StartsWith("stu-families-test-", StringComparison.Ordinal)) Directory.Delete(storage, true);
    }

    [Fact]
    public async Task ImportCreatesSeparatePropertiesFamiliesLinksAndExportsCurrentResidence()
    {
        var setup = await FamilyTestData.CreateAsync(factory);
        var code = await CodeAsync(setup.MicroregionId);
        var jobId = await ImportAsync(setup, $"{Header}\n{code},Rua Importada,10,F-100,Ana-María D'Ávila,-39.6,-17.6,Active,Occupied\n{code},Rua Vazia,11,,,-39.7,-17.7,Active,Vacant");
        await ProcessAsync();
        using var client = await setup.LoginAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StuDbContext>(); var job = await db.OperationJobs.SingleAsync(j => j.Id == jobId);
            Assert.Equal(OperationJobStatus.AwaitingApproval, job.Status); Assert.Equal(2, job.RecordCount); Assert.Equal(1, job.FamilyCount); Assert.Equal(1, job.LinkCount);
            Assert.Equal(3, await db.Properties.CountAsync(p => p.HealthUnitId == setup.UnitId)); Assert.Equal(0, await db.Families.CountAsync(f => f.HealthUnitId == setup.UnitId));
        }
        using var approved = await FamilyTestData.SendAsync(client, HttpMethod.Post, $"/api/operations/imports/{jobId}/approve", null);
        Assert.Equal(HttpStatusCode.Accepted, approved.StatusCode); await ProcessAsync();
        Guid familyId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
            Assert.Equal(OperationJobStatus.Completed, (await db.OperationJobs.SingleAsync(j => j.Id == jobId)).Status);
            Assert.Equal(5, await db.Properties.CountAsync(p => p.HealthUnitId == setup.UnitId));
            var family = await db.Families.SingleAsync(f => f.HealthUnitId == setup.UnitId); familyId = family.Id;
            Assert.Equal("Ana-María D'Ávila", family.ResponsibleName); Assert.Single(await db.FamilyPropertyLinks.Where(l => l.FamilyId == family.Id).ToListAsync());
        }
        // Export after moving must refer to the new property, while the imported address stays vacant.
        await FamilyTestData.LinkAsync(client, familyId, setup.PropertyIds[0]);
        foreach (var format in new[] { OperationFileFormat.Csv, OperationFileFormat.GeoJson, OperationFileFormat.Kml })
        {
            Guid exportId;
            using (var scope = factory.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<StuDbContext>(); var job = OperationJob.CreateExport(setup.UnitId, setup.ActorId, format, "{}"); db.OperationJobs.Add(job); await db.SaveChangesAsync(); exportId = job.Id; }
            await ProcessAsync();
            using var resultScope = factory.Services.CreateScope(); var resultDb = resultScope.ServiceProvider.GetRequiredService<StuDbContext>(); var exported = await resultDb.OperationJobs.SingleAsync(j => j.Id == exportId);
            Assert.Equal(OperationJobStatus.Completed, exported.Status);
            var content = await File.ReadAllTextAsync(Path.Combine(storage, exported.ResultFileName!));
            Assert.Contains("propertyId", content); Assert.Contains("familyId", content); Assert.Contains("linkId", content);
            Assert.Contains(familyId.ToString(), content);
            if (format == OperationFileFormat.GeoJson) { using var document = JsonDocument.Parse(content); var row = document.RootElement.GetProperty("features").EnumerateArray().Single(f => f.GetProperty("id").GetGuid() == setup.PropertyIds[0]); Assert.Equal("F-100", row.GetProperty("properties").GetProperty("familyNumber").GetString()); Assert.Equal("Ana-María D'Ávila", row.GetProperty("properties").GetProperty("familyResponsibleName").GetString()); }
        }
    }

    [Fact]
    public async Task PartialFamilyIsRejectedAndConflictAfterPreviewRollsBackEveryRow()
    {
        var setup = await FamilyTestData.CreateAsync(factory); var code = await CodeAsync(setup.MicroregionId);
        var partialId = await ImportAsync(setup, $"{Header}\n{code},Rua Parcial,10,F-1,,-39.6,-17.6,Active,Occupied"); await ProcessAsync();
        using (var scope = factory.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<StuDbContext>(); var job = await db.OperationJobs.SingleAsync(j => j.Id == partialId); Assert.Equal(OperationJobStatus.Failed, job.Status); Assert.Contains("juntos", job.ErrorSummary); Assert.Equal(3, await db.Properties.CountAsync(p => p.HealthUnitId == setup.UnitId)); }
        var jobId = await ImportAsync(setup, $"{Header}\n{code},Rua Importada,20,F-2,Responsável sintético,-39.6,-17.6,Active,Occupied\n{code},Rua Outra,21,,,-39.7,-17.7,Active,Vacant"); await ProcessAsync();
        using var client = await setup.LoginAsync(factory); await FamilyTestData.CreateFamilyAsync(client, "F-2", "Criada após prévia");
        using var approved = await FamilyTestData.SendAsync(client, HttpMethod.Post, $"/api/operations/imports/{jobId}/approve", null); Assert.Equal(HttpStatusCode.Accepted, approved.StatusCode); await ProcessAsync();
        using var finalScope = factory.Services.CreateScope(); var finalDb = finalScope.ServiceProvider.GetRequiredService<StuDbContext>();
        Assert.Equal(OperationJobStatus.Failed, (await finalDb.OperationJobs.SingleAsync(j => j.Id == jobId)).Status);
        Assert.Equal(3, await finalDb.Properties.CountAsync(p => p.HealthUnitId == setup.UnitId)); Assert.Equal(1, await finalDb.Families.CountAsync(f => f.HealthUnitId == setup.UnitId)); Assert.Equal(0, await finalDb.FamilyPropertyLinks.CountAsync(l => l.HealthUnitId == setup.UnitId));
    }

    private const string Header = "microregionCode,street,houseNumber,familyNumber,familyResponsibleName,longitude,latitude,registrationStatus,situation";
    private async Task<string> CodeAsync(Guid id) { using var scope = factory.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<StuDbContext>().Microregions.Where(m => m.Id == id).Select(m => m.Code).SingleAsync(); }
    private async Task<Guid> ImportAsync(FamilyTestData setup, string csv)
    {
        var name = Guid.NewGuid().ToString("N") + ".csv"; await File.WriteAllTextAsync(Path.Combine(storage, name), csv);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<StuDbContext>(); var job = OperationJob.CreateImport(setup.UnitId, setup.ActorId, OperationFileFormat.Csv, name, "synthetic.csv"); db.OperationJobs.Add(job); await db.SaveChangesAsync(); return job.Id;
    }
    private async Task ProcessAsync()
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<StuDbContext>();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Operations:StoragePath"] = storage }).Build();
        Assert.True(await new OperationJobProcessor(db, config, NullLogger<OperationJobProcessor>.Instance).ProcessNextAsync(CancellationToken.None));
    }
}
