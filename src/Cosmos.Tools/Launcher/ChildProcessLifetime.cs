using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Cosmos.Tools.Launcher;

/// <summary>
/// Ensures a spawned child process dies when this (parent) process exits, by
/// any means — clean exit, Ctrl-C, SIGTERM, or hard kill from a grandparent
/// (e.g. VS Code calling Process.kill on us). Without this, killing cosmos.exe
/// from outside leaves QEMU as an orphan because TerminateProcess on Windows
/// and uncaught SIGKILL on Unix don't run any cleanup code.
///
/// Strategy:
///  - Windows: wrap the child in a Job Object with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE.
///    When the only handle to the job (held by us) closes — for any reason
///    including TerminateProcess — the OS kills every process in the job.
///    No managed cleanup code involved.
///  - Unix:    register PosixSignalRegistration handlers for SIGINT/SIGTERM/SIGHUP.
///    Each kills the child tree, then Environment.Exit with the conventional
///    128+signum code. SIGKILL still orphans (no handler possible).
///
/// Deliberately does NOT use AppDomain.ProcessExit: that fires after
/// `using var process` has disposed the Process object, and Process.Kill(true)
/// on a disposed Process can degenerate to kill(0, SIGKILL) — which signals
/// the entire process group, taking cosmos.exe down with SIGKILL and turning
/// a clean exit into a signal-termination (Node sees `code=null`).
/// </summary>
public sealed class ChildProcessLifetime : IDisposable
{
    private readonly Process _child;
    private readonly List<IDisposable> _signalHandlers = new();
    private IntPtr _jobHandle = IntPtr.Zero;
    private bool _disposed;

    private ChildProcessLifetime(Process child)
    {
        _child = child;
    }

    public static ChildProcessLifetime AttachTo(Process child)
    {
        var lifetime = new ChildProcessLifetime(child);

        if (OperatingSystem.IsWindows())
        {
            lifetime._jobHandle = WindowsJobObject.CreateKillOnCloseJob();
            WindowsJobObject.AssignProcess(lifetime._jobHandle, child.Handle);
        }
        else
        {
            lifetime._signalHandlers.Add(PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx =>
            {
                lifetime.KillChild();
                ctx.Cancel = true;
                Environment.Exit(130);
            }));
            lifetime._signalHandlers.Add(PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
            {
                lifetime.KillChild();
                ctx.Cancel = true;
                Environment.Exit(143);
            }));
            lifetime._signalHandlers.Add(PosixSignalRegistration.Create(PosixSignal.SIGHUP, ctx =>
            {
                lifetime.KillChild();
                ctx.Cancel = true;
                Environment.Exit(129);
            }));
        }

        return lifetime;
    }

    private void KillChild()
    {
        // After Dispose, _child may be a disposed Process — Kill(true) on a
        // disposed handle can collapse to kill(0, SIGKILL) on Unix, killing
        // ourselves. Bail before touching it.
        if (_disposed)
        {
            return;
        }
        try
        {
            if (!_child.HasExited)
            {
                _child.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort — child may have already exited or be unkillable.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        foreach (IDisposable handler in _signalHandlers)
        {
            handler.Dispose();
        }
        if (_jobHandle != IntPtr.Zero && OperatingSystem.IsWindows())
        {
            // Closing the job handle triggers KILL_ON_JOB_CLOSE if the child
            // is still alive. Safe to call even if already disposed.
            WindowsJobObject.CloseHandle(_jobHandle);
            _jobHandle = IntPtr.Zero;
        }
    }
}

[SupportedOSPlatform("windows")]
internal static class WindowsJobObject
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob, int JobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    public static IntPtr CreateKillOnCloseJob()
    {
        IntPtr job = CreateJobObjectW(IntPtr.Zero, null);
        if (job == IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObjectW failed");
        }

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

        int size = Marshal.SizeOf(info);
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, fDeleteOld: false);
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ptr, (uint)size))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "SetInformationJobObject failed");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return job;
    }

    public static void AssignProcess(IntPtr job, IntPtr processHandle)
    {
        if (!AssignProcessToJobObject(job, processHandle))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "AssignProcessToJobObject failed");
        }
    }
}
