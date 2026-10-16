namespace SimpleERP.Web.Services;

/// <summary>
/// How long an Admin session survives without activity (R7, HC 2026-10-16: 30 minutes).
/// Configurable as Security:AdminIdleMinutes only so the regression harness can prove the
/// sign-out without waiting half an hour; production keeps the default.
/// </summary>
public static class SessionPolicy
{
    public const int DefaultAdminIdleMinutes = 30;
    public static TimeSpan AdminIdle(IConfiguration config) =>
        TimeSpan.FromMinutes(Math.Clamp(config.GetValue("Security:AdminIdleMinutes", DefaultAdminIdleMinutes), 1, 240));
}
