using Microsoft.Win32;

namespace KinahLedger.Platform;

/// <summary>
/// "Start with Windows": the per-user Run entry (no admin rights needed), launching the exe with --background so it
/// waits in the tray for the game. Task Manager's Startup tab can switch an entry off without removing it; that counts
/// as off here, and switching it back on in Options clears Task Manager's flag too.
/// </summary>
public static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    public const string ValueName = "Kinah Ledger";
    public const string BackgroundArg = "--background";

    public static string Command(string exe) => $"\"{exe}\" {BackgroundArg}";

    /// <summary>True when Windows will start the tracker at sign-in.</summary>
    public static bool IsOn()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            if (k?.GetValue(ValueName) is not string s || s.Length == 0) return false;
            return !DisabledInTaskManager();
        }
        catch (Exception) { return false; }
    }

    /// <summary>Task Manager stores 02… for enabled and 03… for disabled (06/07 on some builds): an odd first byte is off.</summary>
    static bool DisabledInTaskManager()
    {
        using var a = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        return a?.GetValue(ValueName) is byte[] b && b.Length > 0 && (b[0] & 1) == 1;
    }

    /// <summary>Turns the entry on (pointing at <paramref name="exe"/>) or off. False, with the reason, when the registry refused.</summary>
    public static bool Set(bool on, string exe, out string error)
    {
        error = "";
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(ValueName, Command(exe), RegistryValueKind.String);
                else k.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            // a switch-off from Task Manager would otherwise keep winning over the box ticked here
            using (var a = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true))
                a?.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    /// <summary>The exe moved or was renamed since the entry was written: point the entry at where it runs now.</summary>
    public static void Repair(string exe)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (k?.GetValue(ValueName) is string s && s.Length > 0 && !string.Equals(s, Command(exe), StringComparison.OrdinalIgnoreCase))
                k.SetValue(ValueName, Command(exe), RegistryValueKind.String);
        }
        catch (Exception) { /* left as it was; Options shows the state */ }
    }
}
