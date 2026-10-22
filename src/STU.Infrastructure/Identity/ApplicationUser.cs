using Microsoft.AspNetCore.Identity;

namespace STU.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; private set; } = string.Empty;

    public Guid? HealthUnitId { get; set; }

    public bool MustChangePassword { get; private set; }

    public string AppearancePreference { get; private set; } = "light";
    public byte[]? ProfilePhoto { get; private set; }
    public string? ProfilePhotoVersion { get; private set; }
    public DateTimeOffset? PasswordResetRequestedAtUtc { get; private set; }

    public DateTimeOffset? ArchivedAtUtc { get; private set; }

    public bool IsArchived => ArchivedAtUtc.HasValue;

    public static ApplicationUser Create(
        string userName,
        string displayName,
        Guid? healthUnitId,
        bool mustChangePassword = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        return new ApplicationUser
        {
            UserName = userName.Trim(),
            DisplayName = displayName.Trim(),
            HealthUnitId = healthUnitId,
            MustChangePassword = mustChangePassword,
            LockoutEnabled = true,
        };
    }

    public void CompleteTemporaryPasswordChange()
    {
        MustChangePassword = false;
    }

    public void UpdateProfile(string displayName, Guid? healthUnitId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName.Trim();
        HealthUnitId = healthUnitId;
    }

    public void RequireTemporaryPasswordChange()
    {
        MustChangePassword = true;
        PasswordResetRequestedAtUtc = null;
    }

    public void UpdateAccountSettings(string displayName, string appearance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (appearance is not ("light" or "dark")) throw new ArgumentOutOfRangeException(nameof(appearance));
        DisplayName = displayName.Trim();
        AppearancePreference = appearance;
    }

    public void SetProfilePhoto(byte[]? photo)
    {
        ProfilePhoto = photo;
        ProfilePhotoVersion = photo is null ? null : Guid.NewGuid().ToString("N");
    }

    public void RequestPasswordReset() => PasswordResetRequestedAtUtc ??= DateTimeOffset.UtcNow;

    public void Archive()
    {
        ArchivedAtUtc ??= DateTimeOffset.UtcNow;
    }

    public void Restore()
    {
        ArchivedAtUtc = null;
    }
}
