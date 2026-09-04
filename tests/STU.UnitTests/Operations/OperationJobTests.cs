using STU.Domain.Operations;

namespace STU.UnitTests.Operations;

public sealed class OperationJobTests
{
    [Fact]
    public void ImportRequiresValidationAndApprovalBeforeCompletion()
    {
        var actorId = Guid.NewGuid();
        var job = OperationJob.CreateImport(Guid.NewGuid(), actorId, OperationFileFormat.Csv, "source.csv", "imoveis.csv");

        job.Start();
        job.AwaitApproval("stage.json", 25);

        Assert.Equal(OperationJobStatus.AwaitingApproval, job.Status);
        Assert.Equal(25, job.RecordCount);
        Assert.Throws<InvalidOperationException>(() => job.Complete(null, 25));

        job.Approve(actorId);
        job.Start();
        job.Complete(null, 25);

        Assert.Equal(OperationJobStatus.Completed, job.Status);
        Assert.Equal(100, job.ProgressPercentage);
        Assert.Equal(2, job.AttemptCount);
    }

    [Fact]
    public void FailedJobCanOnlyBeRetriedThreeTimes()
    {
        var job = OperationJob.CreateExport(Guid.NewGuid(), Guid.NewGuid(), OperationFileFormat.GeoJson, "{}");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            job.Start();
            job.Fail("Falha temporária");
            if (attempt < 2) job.Retry();
        }

        Assert.Equal(OperationJobStatus.Failed, job.Status);
        Assert.Throws<InvalidOperationException>(job.Retry);
    }
}
