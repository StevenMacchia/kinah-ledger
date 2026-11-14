# Runs a tracker self-test (options-test, ui-shot, ...) on a desktop of its own, so none of its windows, dialogs or
# focus changes ever reach the player's screen. Its output goes to a file, printed when it ends; the exit code is the test's.
#
#   powershell -ExecutionPolicy Bypass -File tests\hidden-run.ps1 <path\to\KinahLedger.exe> options-test
#   powershell -ExecutionPolicy Bypass -File tests\hidden-run.ps1 <path\to\KinahLedger.exe> ui-shot <dir> --timers
#
# (2026-11-14: test windows popped up on Steven's screen while he played.) options-test and ui-shot also register no
# shortcuts (Native.NoHotkeys), so they never take the installed tracker's.
param([Parameter(Mandatory)][string]$Exe, [Parameter(ValueFromRemainingArguments)][string[]]$Rest)
$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class HiddenDesk
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO { public int cb; public string lpReserved, lpDesktop, lpTitle; public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags; public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError; }
    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr devmode, int flags, uint access, IntPtr sa);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcess(string app, string cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr h, out uint code);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool SetHandleInformation(IntPtr h, uint mask, uint flags);
    // the test's output: straight to a file the child inherits (a GUI exe under cmd /c would not be waited for)
    public static int Run(string name, string cmd, string dir, string outFile)
    {
        using (var fs = new System.IO.FileStream(outFile, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite))
        {
        IntPtr h = fs.SafeFileHandle.DangerousGetHandle();
        SetHandleInformation(h, 1 /* HANDLE_FLAG_INHERIT */, 1);
        IntPtr desk = CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, 0x10000000u /* GENERIC_ALL */, IntPtr.Zero);
        if (desk == IntPtr.Zero) throw new Exception("CreateDesktop failed: " + Marshal.GetLastWin32Error());
        try
        {
            PROCESS_INFORMATION pi;
            var si = new STARTUPINFO { cb = Marshal.SizeOf(typeof(STARTUPINFO)), lpDesktop = name, dwFlags = 0x100 /* STARTF_USESTDHANDLES */, hStdOutput = h, hStdError = h };
            if (!CreateProcess(null, cmd, IntPtr.Zero, IntPtr.Zero, true, 0x08000000 /* CREATE_NO_WINDOW */, IntPtr.Zero, dir, ref si, out pi))
                throw new Exception("CreateProcess failed: " + Marshal.GetLastWin32Error());
            WaitForSingleObject(pi.hProcess, 0xFFFFFFFF);
            uint code; GetExitCodeProcess(pi.hProcess, out code);
            CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
            return (int)code;
        }
        finally { CloseDesktop(desk); }
        }
    }
}
'@
$exePath = (Resolve-Path $Exe).Path
$out = Join-Path ([IO.Path]::GetTempPath()) ("kl-hidden-" + [guid]::NewGuid().ToString('N') + ".txt")
$quoted = ($Rest | ForEach-Object { if ($_ -match '[\s"]') { '"' + $_.Replace('"', '\"') + '"' } else { $_ } }) -join ' '
$code = [HiddenDesk]::Run("KinahLedgerTest", "`"$exePath`" $quoted", (Get-Location).Path, $out)
if (Test-Path $out) { Get-Content $out; Remove-Item $out }
exit $code
