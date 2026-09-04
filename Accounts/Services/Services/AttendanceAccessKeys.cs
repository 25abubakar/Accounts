namespace Accounts.Services.Services;

/// <summary>
/// Attendance feature-key suffixes under MENU_{id}_*.
/// Legacy OWN_HISTORY / TEAM_HISTORY remain recognized for migration safety.
/// </summary>
public static class AttendanceAccessKeys
{
    public const string ViewSelf = "VIEW_SELF";
    public const string CurrentMonth = "CURRENT_MONTH";
    public const string PreviousMonths = "PREVIOUS_MONTHS";
    public const string ViewEmployees = "VIEW_EMPLOYEES";
    public const string ViewAllEmployees = "VIEW_ALL_EMPLOYEES";

    // Legacy person-extra suffixes (Access Control UI / AccessFeatures).
    public const string LegacyOwnHistory = "OWN_HISTORY";
    public const string LegacyTeamHistory = "TEAM_HISTORY";
    public const string LegacyHistory = "HISTORY";

    public static readonly string[] AttendanceMenuRoutes =
    [
        "/attendance",
        "/attendance/staff",
        "/attendance/daily-report",
        "/attendance/report",
        "/attendance/remote",
        "/attendance/login",
        "/attendance/monthly-chart",
        "/attendance/timing-chart",
        "/attendance/by-supervisor",
        "/attendance/camera",
        "/attendance/check-in"
    ];

    public static string FeatureKey(int menuId, string suffix) =>
        $"MENU_{menuId}_{suffix}";

    public static bool HasSuffix(IEnumerable<string> keys, string suffix) =>
        keys.Any(key =>
            key.StartsWith("MENU_", StringComparison.OrdinalIgnoreCase) &&
            key.EndsWith("_" + suffix, StringComparison.OrdinalIgnoreCase));

    public static string DisplayName(string suffix) => suffix switch
    {
        ViewSelf => "View Self Attendance",
        CurrentMonth => "View Current Month",
        PreviousMonths => "View Previous Months",
        ViewEmployees => "View Employee Attendance",
        ViewAllEmployees => "View All Employees Attendance",
        LegacyOwnHistory => "View Own Previous Months",
        LegacyTeamHistory => "View Team Previous Months",
        _ => suffix.Replace('_', ' ')
    };
}
