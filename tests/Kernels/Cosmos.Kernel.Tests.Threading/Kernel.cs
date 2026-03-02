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

    // Custom delegate types for delegate tests
    private delegate void VoidDelegate();
    private delegate int BinaryIntDelegate(int a, int b);

    protected override void BeforeRun()
    {
        Serial.WriteString("[Threading] BeforeRun() reached!\n");
        Serial.WriteString("[Threading] Starting tests...\n");

        TR.Start("Threading Tests", expectedTests: 34);

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

        // Delegate tests
        TR.Run("Delegate_Action_BasicInvoke", TestDelegateActionBasicInvoke);
        TR.Run("Delegate_Func_ReturnsValue", TestDelegateFuncReturnsValue);
        TR.Run("Delegate_ActionT_WithParameter", TestDelegateActionWithParameter);
        TR.Run("Delegate_FuncT_Transform", TestDelegateFuncTransform);
        TR.Run("Delegate_CustomType_VoidNoParam", TestDelegateCustomVoid);
        TR.Run("Delegate_CustomType_WithReturn", TestDelegateCustomWithReturn);
        TR.Run("Delegate_StaticMethod", TestDelegateStaticMethod);
        TR.Run("Delegate_InstanceMethod", TestDelegateInstanceMethod);
        TR.Run("Delegate_Multicast_BothCalled", TestDelegateMulticastBothCalled);
        TR.Run("Delegate_Multicast_InvocationOrder", TestDelegateMulticastOrder);
        TR.Run("Delegate_Multicast_Remove", TestDelegateMulticastRemove);
        TR.Run("Delegate_Multicast_GetInvocationList", TestDelegateMulticastGetInvocationList);
        TR.Run("Delegate_Closure_CapturesLocal", TestDelegateClosureCapturesLocal);
        TR.Run("Delegate_Closure_MutableCapture", TestDelegateClosureMutableCapture);
        TR.Run("Delegate_Closure_SharedCapture", TestDelegateClosureSharedCapture);
        TR.Run("Delegate_Null_SafeInvoke", TestDelegateNullSafeInvoke);
        TR.Run("Delegate_Equality_SameMethod", TestDelegateEqualitySameMethod);
        TR.Run("Delegate_Equality_DifferentMethod", TestDelegateEqualityDifferentMethod);
        TR.Run("Delegate_AsParameter", TestDelegateAsParameter);
        TR.Run("Delegate_AsReturnValue", TestDelegateAsReturnValue);
        TR.Run("Delegate_Generic_ValueType", TestDelegateGenericValueType);
        TR.Run("Delegate_Predicate", TestDelegatePredicate);
        TR.Run("Delegate_Comparison", TestDelegateComparison);
        TR.Run("Delegate_Chaining_Pipeline", TestDelegateChaining);
        TR.Run("Delegate_EventPattern_Multicast", TestDelegateEventPattern);

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

        TimerManager.Wait(100); // Wait 10ms for the thread to finish.

        Assert.Equal(18, StaticValue);
        Assert.Equal(42, secondThreadValue);
    }

    // ==================== Delegate Tests ====================

    // --- Basic invocation ---

    private static void TestDelegateActionBasicInvoke()
    {
        bool invoked = false;
        Action action = () => { invoked = true; };
        action();
        Assert.True(invoked, "Action delegate should set invoked flag when called");
    }

    private static void TestDelegateFuncReturnsValue()
    {
        Func<int> getAnswer = () => 42;
        int result = getAnswer();
        Assert.Equal(42, result, "Func<int> should return 42");
    }

    private static void TestDelegateActionWithParameter()
    {
        int received = 0;
        Action<int> action = (x) => { received = x; };
        action(99);
        Assert.Equal(99, received, "Action<int> should receive and store the parameter");
    }

    private static void TestDelegateFuncTransform()
    {
        Func<int, int> doubler = x => x * 2;
        int result = doubler(21);
        Assert.Equal(42, result, "Func<int,int> should double the input");
    }

    // --- Custom delegate types ---

    private static void TestDelegateCustomVoid()
    {
        bool called = false;
        VoidDelegate d = () => { called = true; };
        d();
        Assert.True(called, "Custom void delegate should be invoked");
    }

    private static void TestDelegateCustomWithReturn()
    {
        BinaryIntDelegate add = (a, b) => a + b;
        int result = add(10, 32);
        Assert.Equal(42, result, "Custom BinaryIntDelegate should add the two parameters");
    }

    // --- Static and instance method delegates ---

    private static int StaticMultiply(int x, int y) => x * y;

    private static void TestDelegateStaticMethod()
    {
        Func<int, int, int> multiply = StaticMultiply;
        int result = multiply(6, 7);
        Assert.Equal(42, result, "Delegate bound to static method should compute 6*7=42");
    }

    private class DelegateAccumulator
    {
        public int Total { get; private set; }
        public void Add(int value) => Total += value;
    }

    private static void TestDelegateInstanceMethod()
    {
        var accumulator = new DelegateAccumulator();
        Action<int> add = accumulator.Add;
        add(10);
        add(32);
        Assert.Equal(42, accumulator.Total, "Instance method delegate should accumulate values into the bound object");
    }

    // --- Multicast delegates ---

    private static void TestDelegateMulticastBothCalled()
    {
        int callCount = 0;
        Action a = () => { callCount++; };
        Action b = () => { callCount++; };
        Action combined = a + b;
        combined();
        Assert.Equal(2, callCount, "Multicast delegate should invoke both handlers");
    }

    private static void TestDelegateMulticastOrder()
    {
        // Verify that multicast delegates invoke handlers in registration order
        int[] log = new int[3];
        int index = 0;

        Action first  = () => { log[index] = 1; index++; };
        Action second = () => { log[index] = 2; index++; };
        Action third  = () => { log[index] = 3; index++; };

        Action combined = first + second + third;
        combined();

        Assert.Equal(1, log[0], "First handler should be invoked first");
        Assert.Equal(2, log[1], "Second handler should be invoked second");
        Assert.Equal(3, log[2], "Third handler should be invoked third");
    }

    private static void TestDelegateMulticastRemove()
    {
        int callCount = 0;
        Action a = () => { callCount++; };
        Action b = () => { callCount += 10; };

        Action combined = a + b;
        combined -= b;
        combined();

        // Only 'a' should remain: callCount == 1, not 11
        Assert.Equal(1, callCount, "After removing handler b, only handler a should fire");
    }

    private static void TestDelegateMulticastGetInvocationList()
    {
        Action a = () => { };
        Action b = () => { };
        Action c = () => { };

        Action combined = a + b + c;
        Delegate[] list = combined.GetInvocationList();

        Assert.Equal(3, list.Length, "GetInvocationList should return 3 delegates after combining three");
    }

    // --- Closures ---

    private static void TestDelegateClosureCapturesLocal()
    {
        int x = 10;
        Func<int> getX = () => x;
        int result = getX();
        Assert.Equal(10, result, "Closure should capture the local variable value at invocation time");
    }

    private static void TestDelegateClosureMutableCapture()
    {
        // Lambda mutates the captured variable; outer scope sees the change
        int counter = 0;
        Action increment = () => { counter++; };

        increment();
        increment();
        increment();

        Assert.Equal(3, counter, "Closure should mutate the captured variable; outer scope should see 3");
    }

    private static void TestDelegateClosureSharedCapture()
    {
        // Two distinct lambdas capturing the same local variable share the same closure slot
        int shared = 0;
        Action addTen = () => { shared += 10; };
        Action addFive = () => { shared += 5; };

        addTen();
        addFive();

        Assert.Equal(15, shared, "Both closures sharing a captured variable should both modify it (10 + 5 = 15)");
    }

    // --- Null delegate ---

    private static void TestDelegateNullSafeInvoke()
    {
        // ?. on a null delegate must not throw; it's a no-op
        Action? nullDelegate = null;
        nullDelegate?.Invoke();
        // Reaching here without a fault means the test passes
        Assert.True(true, "Null?.Invoke() should be a safe no-op and not fault");
    }

    // --- Delegate equality ---

    private static void DelegateEqualityTarget1() { }
    private static void DelegateEqualityTarget2() { }

    private static void TestDelegateEqualitySameMethod()
    {
        // Two delegates wrapping the same static method must compare equal
        Action a = DelegateEqualityTarget1;
        Action b = DelegateEqualityTarget1;
        Assert.True(a == b, "Delegates wrapping the same static method should be equal");
    }

    private static void TestDelegateEqualityDifferentMethod()
    {
        // Delegates wrapping different methods must compare unequal
        Action a = DelegateEqualityTarget1;
        Action b = DelegateEqualityTarget2;
        Assert.True(a != b, "Delegates wrapping different methods should not be equal");
    }

    // --- Delegate as parameter and return value ---

    private static int ApplyTransform(int value, Func<int, int> transform)
    {
        return transform(value);
    }

    private static void TestDelegateAsParameter()
    {
        Func<int, int> square = x => x * x;
        int result = ApplyTransform(7, square);
        Assert.Equal(49, result, "Delegate passed as parameter should be invoked: 7*7=49");
    }

    private static Func<int, int> CreateAdder(int amount)
    {
        return x => x + amount;
    }

    private static void TestDelegateAsReturnValue()
    {
        // CreateAdder captures 'amount' in a closure and returns the delegate
        Func<int, int> addTen = CreateAdder(10);
        int result = addTen(32);
        Assert.Equal(42, result, "Factory-returned delegate should close over 'amount': 32+10=42");
    }

    // --- Generic delegates with value types ---

    private static void TestDelegateGenericValueType()
    {
        Func<long, long> negate = x => -x;
        long result = negate(42L);
        Assert.Equal(-42L, result, "Generic Func<long,long> should negate the input");
    }

    // --- Predicate<T> ---

    private static void TestDelegatePredicate()
    {
        Predicate<int> isEven = x => (x % 2) == 0;

        Assert.True(isEven(4),  "Predicate: 4 should be even");
        Assert.False(isEven(7), "Predicate: 7 should be odd");
        Assert.True(isEven(0),  "Predicate: 0 should be even");
        Assert.False(isEven(1), "Predicate: 1 should be odd");
    }

    // --- Comparison<T> ---

    private static void TestDelegateComparison()
    {
        // Descending comparator: larger value sorts first
        Comparison<int> descending = (a, b) => b - a;

        // a=5, b=3 → b-a = -2 < 0 → a (5) comes before b (3) in descending order ✓
        int result = descending(5, 3);
        Assert.True(result < 0, "Descending comparison: compare(5,3) should be negative (5 before 3)");

        result = descending(3, 5);
        Assert.True(result > 0, "Descending comparison: compare(3,5) should be positive (3 after 5)");

        result = descending(4, 4);
        Assert.Equal(0, result, "Descending comparison: compare(4,4) should be zero (equal)");
    }

    // --- Delegate chaining / composition ---

    private static void TestDelegateChaining()
    {
        Func<int, int> addOne         = x => x + 1;
        Func<int, int> multiplyByThree = x => x * 3;

        // Manual pipeline: (13 + 1) * 3 = 42
        Func<int, int> pipeline = x => multiplyByThree(addOne(x));
        int result = pipeline(13);
        Assert.Equal(42, result, "Composed pipeline (13+1)*3 should equal 42");
    }

    // --- Event-style multicast pattern ---

    private static void TestDelegateEventPattern()
    {
        int eventFireCount = 0;
        string? lastEventData = null;

        // Simulate an event using a nullable multicast delegate
        Action<string>? handlers = null;

        // Subscribe two handlers
        handlers += (data) => { eventFireCount++; lastEventData = data; };
        handlers += (_)    => { eventFireCount++; };

        // Fire the event
        handlers?.Invoke("hello");

        Assert.Equal(2, eventFireCount, "Both event handlers should fire");
        Assert.Equal("hello", lastEventData, "First handler should receive the event payload");
    }
}
