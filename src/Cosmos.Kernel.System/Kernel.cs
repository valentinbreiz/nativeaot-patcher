using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Logging;
using Cosmos.Kernel.HAL.Cpu;

namespace Cosmos.Kernel.System;

/// <summary>
/// Base class for all Cosmos user kernels.
/// Provides the BeforeRun/Run/AfterRun lifecycle pattern.
/// </summary>
[Logger]
public abstract partial class Kernel
{
    protected bool mStarted;
    protected bool mStopped;

    /// <summary>
    /// Constructs a new Kernel instance.
    /// </summary>
    public Kernel()
    {
        Log.Debug("Constructing Cosmos.Kernel.System.Kernel instance");
    }

    /// <summary>
    /// Starts the kernel lifecycle.
    /// Called by the generated entry point.
    /// </summary>
    public virtual void Start()
    {
        Log.Info("Starting kernel...");

        Log.Debug("Calling OnBoot()");
        OnBoot();

        if (InterruptManager.IsEnabled)
        {
            Log.Debug("Enabling interrupts");
            InternalCpu.EnableInterrupts();
        }

        EarlyGop.Enabled = false;

        Log.Debug("Calling BeforeRun()");
        BeforeRun();

        mStarted = true;

        Log.Info("Entering main loop");
        while (!mStopped)
        {
            Log.Trace("Calling Run()");
            Run();
            Log.Trace("Run() returned");
        }

        Log.Info("Main loop exited, calling AfterRun()");
        AfterRun();

        // Halt the CPU to prevent returning to NativeAOT shutdown sequence
        // The shutdown code tries to allocate memory which fails in kernel environment
        Log.Info("Halting CPU");
        while (true)
        {
            InternalCpu.Halt();
        }
    }

    /// <summary>
    /// Called once during boot, before BeforeRun().
    /// Override to customize system initialization.
    /// </summary>
    protected virtual void OnBoot()
    {
        Global.Init();
    }

    /// <summary>
    /// Called once before the main loop starts.
    /// Override to perform one-time setup.
    /// </summary>
    protected virtual void BeforeRun()
    {
    }

    /// <summary>
    /// Called repeatedly in the main loop.
    /// Override to implement your kernel's main logic.
    /// </summary>
    protected abstract void Run();

    /// <summary>
    /// Called once after the main loop exits.
    /// Override to perform cleanup.
    /// </summary>
    protected virtual void AfterRun()
    {
    }

    /// <summary>
    /// Signals the kernel to stop the main loop.
    /// </summary>
    public void Stop()
    {
        mStopped = true;
    }
}
