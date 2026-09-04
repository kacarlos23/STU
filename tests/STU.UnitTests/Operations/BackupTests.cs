using STU.Domain.Operations;

namespace STU.UnitTests.Operations;

public sealed class BackupTests
{
    [Fact]
    public void DefaultSettingsScheduleAWeeklySlotInBahiaTime()
    {
        var settings = BackupSettings.CreateDefault();

        var slot = settings.MostRecentSlot(new DateTimeOffset(2026, 8, 24, 13, 0, 0, TimeSpan.Zero));

        Assert.True(settings.Enabled);
        Assert.Equal(DayOfWeek.Sunday, settings.DayOfWeek);
        Assert.Equal("2026-08-23", slot.Key);
        Assert.Equal(new DateTimeOffset(2026, 8, 23, 3, 0, 0, TimeSpan.Zero), slot.ScheduledAtUtc);
    }

    [Fact]
    public void SettingsValidateHourAndRetention()
    {
        var settings = BackupSettings.CreateDefault();

        Assert.Throws<ArgumentOutOfRangeException>(() => settings.Update(true, DayOfWeek.Monday, 24, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.Update(true, DayOfWeek.Monday, 2, 0));

        settings.Update(false, DayOfWeek.Friday, 21, 12);
        Assert.False(settings.Enabled);
        Assert.Equal(DayOfWeek.Friday, settings.DayOfWeek);
        Assert.Equal(21, settings.LocalHour);
        Assert.Equal(12, settings.RetentionCount);
    }

    [Fact]
    public void BackupRunKeepsIntegrityMetadataAndStateTransitions()
    {
        var run = BackupRun.CreateManual(Guid.NewGuid());

        run.Start();
        run.Complete("stu-test.dump", 512, new string('a', 64));

        Assert.Equal(BackupRunStatus.Completed, run.Status);
        Assert.Equal(1, run.AttemptCount);
        Assert.Equal(512, run.SizeBytes);
        Assert.Equal(new string('a', 64), run.Sha256);
        Assert.NotNull(run.CompletedAtUtc);

        run.MarkFilePruned();
        Assert.NotNull(run.FilePrunedAtUtc);
    }
}
