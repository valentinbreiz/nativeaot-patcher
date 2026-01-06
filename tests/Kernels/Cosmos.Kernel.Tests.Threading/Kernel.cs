using System;
using System.Threading;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.System.Timer;
using Cosmos.TestRunner.Framework;
using static Cosmos.TestRunner.Framework.TestRunner;
using static Cosmos.TestRunner.Framework.Assert;

namespace Cosmos.Kernel.Tests.Threading
{
    internal unsafe static partial class Program
    {
        // Shared state for thread tests
        private static volatile bool _threadExecuted;
        private static volatile int _sharedCounter;
        private static volatile int _thread1Counter;
        private static volatile int _thread2Counter;
        private static Cosmos.Kernel.Core.Scheduler.SpinLock _testLock;

        /// <summary>
        /// Main test kernel entry point
        /// </summary>
        private static void Main()
        {
            Serial.WriteString("[Threading] Main() reached!\n");
            Serial.WriteString("[Threading] Starting tests...\n");

            // Initialize test suite
            Start("Threading Tests", expectedTests: 9);

            // SpinLock tests
            RunSpinLockTests();

            // Thread tests
            RunThreadTests();

            // Finish test suite
            Finish();

            Serial.WriteString("\n[Tests Complete - System Halting]\n");

            // Halt the system
            while (true) ;
        }

        private static void RunSpinLockTests()
        {
            // Test 1: SpinLock initial state
            Run("SpinLock_InitialState_IsUnlocked", () =>
            {
                var spinLock = new Cosmos.Kernel.Core.Scheduler.SpinLock();
                False(spinLock.IsLocked, "New spinlock should be unlocked");
            });

            // Test 2: SpinLock acquire sets locked state
            Run("SpinLock_Acquire_SetsLockedState", () =>
            {
                var spinLock = new Cosmos.Kernel.Core.Scheduler.SpinLock();
                spinLock.Acquire();
                True(spinLock.IsLocked, "Spinlock should be locked after Acquire");
                spinLock.Release();
            });

            // Test 3: SpinLock release clears locked state
            Run("SpinLock_Release_ClearsLockedState", () =>
            {
                var spinLock = new Cosmos.Kernel.Core.Scheduler.SpinLock();
                spinLock.Acquire();
                spinLock.Release();
                False(spinLock.IsLocked, "Spinlock should be unlocked after Release");
            });

            // Test 4: TryAcquire succeeds on unlocked
            Run("SpinLock_TryAcquire_SucceedsOnUnlocked", () =>
            {
                var spinLock = new Cosmos.Kernel.Core.Scheduler.SpinLock();
                bool acquired = spinLock.TryAcquire();
                True(acquired, "TryAcquire should succeed on unlocked spinlock");
                True(spinLock.IsLocked, "Spinlock should be locked after TryAcquire succeeds");
                spinLock.Release();
            });

            // Test 5: TryAcquire fails on locked (simulated with manual lock state)
            Run("SpinLock_TryAcquire_FailsOnLocked", () =>
            {
                var spinLock = new Cosmos.Kernel.Core.Scheduler.SpinLock();
                spinLock.Acquire();
                // Try to acquire from same context - should fail
                bool acquired = spinLock.TryAcquire();
                False(acquired, "TryAcquire should fail on already locked spinlock");
                spinLock.Release();
            });
        }

        private static void RunThreadTests()
        {
            // Test 6: Thread creation
            Run("Thread_Creation_Succeeds", () =>
            {
                Serial.WriteString("[Test] Creating thread...\n");
                var thread = new System.Threading.Thread(() =>
                {
                    Serial.WriteString("[Test] Thread delegate - doing nothing\n");
                });
                NotNull(thread);
                Serial.WriteString("[Test] Thread created successfully\n");
            });

            // Test 7: Thread execution
            Run("Thread_Start_ExecutesDelegate", () =>
            {
                Serial.WriteString("[Test] Testing thread execution...\n");
                _threadExecuted = false;

                var thread = new System.Threading.Thread(() =>
                {
                    Serial.WriteString("[Thread] Delegate executing!\n");
                    _threadExecuted = true;
                    Serial.WriteString("[Thread] Delegate completed!\n");
                });

                Serial.WriteString("[Test] Starting thread...\n");
                thread.Start();

                // Wait for thread to execute (give scheduler time)
                Serial.WriteString("[Test] Waiting for thread execution...\n");
                TimerManager.Wait(500);

                True(_threadExecuted, "Thread delegate should have executed");
                Serial.WriteString("[Test] Thread execution test complete\n");
            });

            // Test 8: Multiple threads can run
            Run("Thread_Multiple_CanRunConcurrently", () =>
            {
                Serial.WriteString("[Test] Testing multiple threads...\n");
                _thread1Counter = 0;
                _thread2Counter = 0;

                var thread1 = new System.Threading.Thread(() =>
                {
                    Serial.WriteString("[Thread1] Started\n");
                    for (int i = 0; i < 5; i++)
                    {
                        _thread1Counter++;
                        TimerManager.Wait(50);
                    }
                    Serial.WriteString("[Thread1] Completed\n");
                });

                var thread2 = new System.Threading.Thread(() =>
                {
                    Serial.WriteString("[Thread2] Started\n");
                    for (int i = 0; i < 5; i++)
                    {
                        _thread2Counter++;
                        TimerManager.Wait(50);
                    }
                    Serial.WriteString("[Thread2] Completed\n");
                });

                thread1.Start();
                thread2.Start();

                // Wait for both threads to complete
                TimerManager.Wait(1000);

                Serial.WriteString("[Test] Thread1 counter: ");
                Serial.WriteNumber((uint)_thread1Counter);
                Serial.WriteString(", Thread2 counter: ");
                Serial.WriteNumber((uint)_thread2Counter);
                Serial.WriteString("\n");

                Equal(5, _thread1Counter);
                Equal(5, _thread2Counter);
            });

            // Test 9: SpinLock protects shared data across threads
            Run("SpinLock_ProtectsSharedData_AcrossThreads", () =>
            {
                Serial.WriteString("[Test] Testing spinlock with threads...\n");
                _sharedCounter = 0;
                _testLock = new Cosmos.Kernel.Core.Scheduler.SpinLock();

                const int incrementsPerThread = 100;

                var thread1 = new System.Threading.Thread(() =>
                {
                    Serial.WriteString("[Thread1] Starting increments\n");
                    for (int i = 0; i < incrementsPerThread; i++)
                    {
                        _testLock.Acquire();
                        _sharedCounter++;
                        _testLock.Release();
                    }
                    Serial.WriteString("[Thread1] Done\n");
                });

                var thread2 = new System.Threading.Thread(() =>
                {
                    Serial.WriteString("[Thread2] Starting increments\n");
                    for (int i = 0; i < incrementsPerThread; i++)
                    {
                        _testLock.Acquire();
                        _sharedCounter++;
                        _testLock.Release();
                    }
                    Serial.WriteString("[Thread2] Done\n");
                });

                thread1.Start();
                thread2.Start();

                // Wait for threads to complete
                TimerManager.Wait(2000);

                Serial.WriteString("[Test] Final counter: ");
                Serial.WriteNumber((uint)_sharedCounter);
                Serial.WriteString("\n");

                // With proper locking, counter should be exactly 2 * incrementsPerThread
                Equal(incrementsPerThread * 2, _sharedCounter);
            });
        }
    }
}
