using System;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.System.Timer;
using Cosmos.TestRunner.Framework;
using SysThread = System.Threading.Thread;
using Sys = Cosmos.Kernel.System;
using TR = Cosmos.TestRunner.Framework.TestRunner;

namespace Cosmos.Kernel.Tests.Threading;

public class Kernel : Sys.Kernel
{
    // Shared state for thread tests
    private static volatile bool _threadExecuted;
    private static volatile int _sharedCounter;
    private static volatile int _thread1Counter;
    private static volatile int _thread2Counter;
    private static Cosmos.Kernel.Core.Scheduler.SpinLock _testLock;

    protected override void BeforeRun()
    {
        Serial.WriteString("[Threading] BeforeRun() reached!\n");
        Serial.WriteString("[Threading] Starting tests...\n");

        // Initialize test suite - reduced to 8 tests (removed Thread_Creation test that interferes)
        TR.Start("Threading Tests", expectedTests: 9);

        // SpinLock tests
        TR.Run("SpinLock_InitialState_IsUnlocked", TestSpinLockInitialState);
        TR.Run("SpinLock_Acquire_SetsLockedState", TestSpinLockAcquire);
        TR.Run("SpinLock_Release_ClearsLockedState", TestSpinLockRelease);
        TR.Run("SpinLock_TryAcquire_SucceedsOnUnlocked", TestSpinLockTryAcquireSuccess);
        TR.Run("SpinLock_TryAcquire_FailsOnLocked", TestSpinLockTryAcquireFail);

        // Thread tests
        TR.Run("Thread_Start_ExecutesDelegate", TestThreadExecution);
        TR.Run("Thread_Multiple_CanRunConcurrently", TestMultipleThreads);
        TR.Run("SpinLock_ProtectsSharedData_AcrossThreads", TestSpinLockWithThreads);
        TR.Run("Thread_ThreadStatics", TestThreadStatics);

        // Finish test suite
        TR.Finish();

        Serial.WriteString("\n[Tests Complete - System Halting]\n");

        Stop();
    }

    protected override void Run()
    {
        // Tests completed in BeforeRun, nothing to do here
    }

    protected override void AfterRun()
    {
        Cosmos.Kernel.Kernel.Halt();
    }

    // ==================== SpinLock Tests ====================

    private static void TestSpinLockInitialState()
    {
        var spinLock = new Cosmos.Kernel.Core.Scheduler.SpinLock();
        Assert.False(spinLock.IsLocked, "New spinlock should be unlocked");
    }

    private static void TestSpinLockAcquire()
    {
        var spinLock = new Cosmos.Kernel.Core.Scheduler.SpinLock();
        spinLock.Acquire();
        Assert.True(spinLock.IsLocked, "Spinlock should be locked after Acquire");
        spinLock.Release();
    }

    private static void TestSpinLockRelease()
    {
        var spinLock = new Cosmos.Kernel.Core.Scheduler.SpinLock();
        spinLock.Acquire();
        spinLock.Release();
        Assert.False(spinLock.IsLocked, "Spinlock should be unlocked after Release");
    }

    private static void TestSpinLockTryAcquireSuccess()
    {
        var spinLock = new Cosmos.Kernel.Core.Scheduler.SpinLock();
        bool acquired = spinLock.TryAcquire();
        Assert.True(acquired, "TryAcquire should succeed on unlocked spinlock");
        Assert.True(spinLock.IsLocked, "Spinlock should be locked after TryAcquire succeeds");
        spinLock.Release();
    }

    private static void TestSpinLockTryAcquireFail()
    {
        var spinLock = new Cosmos.Kernel.Core.Scheduler.SpinLock();
        spinLock.Acquire();
        // Try to acquire from same context - should fail
        bool acquired = spinLock.TryAcquire();
        Assert.False(acquired, "TryAcquire should fail on already locked spinlock");
        spinLock.Release();
    }

    // ==================== Thread Tests ====================

    private static void TestThreadExecution()
    {
        Serial.WriteString("[Test] Testing thread execution...\n");
        _threadExecuted = false;

        var thread = new global::System.Threading.Thread(ThreadExecutionWorker);

        Serial.WriteString("[Test] Starting thread...\n");
        thread.Start();

        // Wait longer for thread to execute (give scheduler more time)
        Serial.WriteString("[Test] Waiting for thread execution...\n");
        TimerManager.Wait(1000);

        // Check multiple times with delays
        for (int i = 0; i < 5 && !_threadExecuted; i++)
        {
            TimerManager.Wait(200);
        }

        Assert.True(_threadExecuted, "Thread delegate should have executed");
        Serial.WriteString("[Test] Thread execution test complete\n");
    }

    private static void ThreadExecutionWorker()
    {
        Serial.WriteString("[Thread] Delegate executing!\n");
        _threadExecuted = true;
        Serial.WriteString("[Thread] Delegate completed!\n");
    }

    private static void TestMultipleThreads()
    {
        Serial.WriteString("[Test] Testing multiple threads...\n");
        _thread1Counter = 0;
        _thread2Counter = 0;

        var thread1 = new global::System.Threading.Thread(Thread1Worker);
        var thread2 = new global::System.Threading.Thread(Thread2Worker);

        thread1.Start();
        thread2.Start();

        // Wait much longer for both threads to complete (they each do 5 iterations with 50ms waits = 250ms minimum)
        // But scheduler overhead means we need more time
        TimerManager.Wait(3000);

        // Additional waiting if not complete
        for (int i = 0; i < 10 && (_thread1Counter < 5 || _thread2Counter < 5); i++)
        {
            TimerManager.Wait(500);
        }

        Serial.WriteString("[Test] Thread1 counter: ");
        Serial.WriteNumber((uint)_thread1Counter);
        Serial.WriteString(", Thread2 counter: ");
        Serial.WriteNumber((uint)_thread2Counter);
        Serial.WriteString("\n");

        Assert.Equal(5, _thread1Counter);
        Assert.Equal(5, _thread2Counter);
    }

    private static void Thread1Worker()
    {
        Serial.WriteString("[Thread1] Started\n");
        for (int i = 0; i < 5; i++)
        {
            _thread1Counter++;
            TimerManager.Wait(50);
        }
        Serial.WriteString("[Thread1] Completed\n");
    }

    private static void Thread2Worker()
    {
        Serial.WriteString("[Thread2] Started\n");
        for (int i = 0; i < 5; i++)
        {
            _thread2Counter++;
            TimerManager.Wait(50);
        }
        Serial.WriteString("[Thread2] Completed\n");
    }

    private static void TestSpinLockWithThreads()
    {
        Serial.WriteString("[Test] Testing spinlock with threads...\n");
        _sharedCounter = 0;
        _testLock = new Cosmos.Kernel.Core.Scheduler.SpinLock();

        var thread1 = new global::System.Threading.Thread(SpinLockThread1Worker);
        var thread2 = new global::System.Threading.Thread(SpinLockThread2Worker);

        thread1.Start();
        thread2.Start();

        // Wait much longer for threads to complete (100 lock/unlock iterations each)
        TimerManager.Wait(5000);

        // Additional waiting if not complete
        for (int i = 0; i < 10 && _sharedCounter < 200; i++)
        {
            TimerManager.Wait(500);
        }

        Serial.WriteString("[Test] Final counter: ");
        Serial.WriteNumber((uint)_sharedCounter);
        Serial.WriteString("\n");

        // With proper locking, counter should be exactly 200
        Assert.Equal(200, _sharedCounter);
    }

    private static void SpinLockThread1Worker()
    {
        Serial.WriteString("[Thread1] Starting increments\n");
        for (int i = 0; i < 100; i++)
        {
            _testLock.Acquire();
            _sharedCounter++;
            _testLock.Release();
        }
        Serial.WriteString("[Thread1] Done\n");
    }

    private static void SpinLockThread2Worker()
    {
        Serial.WriteString("[Thread2] Starting increments\n");
        for (int i = 0; i < 100; i++)
        {
            _testLock.Acquire();
            _sharedCounter++;
            _testLock.Release();
        }
        Serial.WriteString("[Thread2] Done\n");
    }

    [ThreadStatic]
    private static int StaticValue;
    private static void TestThreadStatics()
    {
        int secondThreadValue = 0;
        StaticValue = 18;

        SysThread thread = new SysThread(() =>
        {
            StaticValue = 42;
            secondThreadValue = StaticValue;
        });

        thread.Start();

        SysThread.Sleep(100); // Wait 10ms for the thread to finish.


        Assert.Equal(18, StaticValue);
        Assert.Equal(42, secondThreadValue);
    }
}
