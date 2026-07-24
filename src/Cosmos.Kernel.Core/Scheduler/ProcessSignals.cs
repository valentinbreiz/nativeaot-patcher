// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.Core.CPU;

namespace Cosmos.Kernel.Core.Scheduler;

public class ProcessSignals
{

    internal ProcessSignals(Process process)
    {
        _process = process;
    }

    private Action?[] Handels = new Action?[31];
    private readonly Process _process;

    public void RegisterHandel(ushort signal, Action func)
    {
        Handels[signal] = func;
    }

    public void Send(ushort signal)
    {
        Action? handel = Handels[signal];
        ProcessSignalConfig config = ProcessSignalConfigs.Configs[signal];
        if (config.KillProcess)
        {
            using (InternalCpu.DisableInterruptsScope())
            {
                foreach (Thread thread in _process.Threads)
                {
                    thread.State = ThreadState.Dead;
                }
            }
        }
        if (handel != null)
        {
            _process.StartThread(new Thread());
            handel();
        }

        if (config.KillProcess)
        {
            _process.Kill(1);
        }
    }
}


public readonly struct ProcessSignalConfig
{
    public readonly ulong TimeOut { get; init; }
    public readonly bool KillProcess { get; init; }
}

public class ProcessSignalConfigs
{

    /// <summary>Hangup detected on controlling terminal.</summary>
    public const int SIGHUP = 1;

    /// <summary>Interrupt from keyboard (Ctrl+C).</summary>
    public const int SIGINT = 2;

    /// <summary>Quit from keyboard.</summary>
    public const int SIGQUIT = 3;

    /// <summary>Illegal instruction.</summary>
    public const int SIGILL = 4;

    /// <summary>Trace or breakpoint trap.</summary>
    public const int SIGTRAP = 5;

    /// <summary>Process abort signal.</summary>
    public const int SIGABRT = 6;

    /// <summary>Bus error.</summary>
    public const int SIGBUS = 7;

    /// <summary>Floating-point exception.</summary>
    public const int SIGFPE = 8;

    /// <summary>
    /// Immediately terminates the process.
    /// This signal cannot be caught, blocked, or ignored.
    /// </summary>
    public const int SIGKILL = 9;

    /// <summary>User-defined signal 1.</summary>
    public const int SIGUSR1 = 10;

    /// <summary>Invalid memory reference (segmentation fault).</summary>
    public const int SIGSEGV = 11;

    /// <summary>User-defined signal 2.</summary>
    public const int SIGUSR2 = 12;

    /// <summary>Write on a pipe with no readers.</summary>
    public const int SIGPIPE = 13;

    /// <summary>Alarm clock signal.</summary>
    public const int SIGALRM = 14;

    /// <summary>
    /// Requests graceful process termination.
    /// Applications should handle this signal to perform cleanup.
    /// </summary>
    public const int SIGTERM = 15;

    /// <summary>Stack fault (Linux-specific).</summary>
    public const int SIGSTKFLT = 16;

    /// <summary>Child process has stopped or exited.</summary>
    public const int SIGCHLD = 17;

    /// <summary>Continue execution if stopped.</summary>
    public const int SIGCONT = 18;

    /// <summary>
    /// Stops the process.
    /// This signal cannot be caught, blocked, or ignored.
    /// </summary>
    public const int SIGSTOP = 19;

    /// <summary>Terminal stop signal (Ctrl+Z).</summary>
    public const int SIGTSTP = 20;

    /// <summary>Background process attempted terminal input.</summary>
    public const int SIGTTIN = 21;

    /// <summary>Background process attempted terminal output.</summary>
    public const int SIGTTOU = 22;

    /// <summary>Urgent condition on a socket.</summary>
    public const int SIGURG = 23;

    /// <summary>CPU time limit exceeded.</summary>
    public const int SIGXCPU = 24;

    /// <summary>File size limit exceeded.</summary>
    public const int SIGXFSZ = 25;

    /// <summary>Virtual timer expired.</summary>
    public const int SIGVTALRM = 26;

    /// <summary>Profiling timer expired.</summary>
    public const int SIGPROF = 27;

    /// <summary>Terminal window size changed.</summary>
    public const int SIGWINCH = 28;

    /// <summary>I/O is now possible.</summary>
    public const int SIGIO = 29;

    /// <summary>
    /// Alias for <see cref="SIGIO"/>.
    /// </summary>
    public const int SIGPOLL = SIGIO;

    /// <summary>Power failure.</summary>
    public const int SIGPWR = 30;

    /// <summary>Bad system call.</summary>
    public const int SIGSYS = 31;

    public static readonly ProcessSignalConfig[] Configs =
    [
        new ProcessSignalConfig()
        {
            TimeOut = 99999,
            KillProcess = true,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 30,
            KillProcess = true,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 30,
            KillProcess = true,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 30,
            KillProcess = true,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 0,
            KillProcess = false,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 0,
            KillProcess = false,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 0,
            KillProcess = false,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 0,
            KillProcess = false,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 0,
            KillProcess = false,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 30,
            KillProcess = true,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 0,
            KillProcess = false,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 0,
            KillProcess = false,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 0,
            KillProcess = false,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 0,
            KillProcess = false,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 0,
            KillProcess = false,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 0,
            KillProcess = false,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 0,
            KillProcess = false,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 0,
            KillProcess = false,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 0,
            KillProcess = false,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 0,
            KillProcess = false,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 0,
            KillProcess = false,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 0,
            KillProcess = false,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 0,
            KillProcess = false,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 0,
            KillProcess = false,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 0,
            KillProcess = false,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 0,
            KillProcess = false,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 0,
            KillProcess = false,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 0,
            KillProcess = false,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 0,
            KillProcess = false,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 0,
            KillProcess = false,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 0,
            KillProcess = false,
        },
        new ProcessSignalConfig()
        {
            TimeOut = 0,
            KillProcess = false,
        }
    ];

}
