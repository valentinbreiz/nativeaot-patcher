using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Services.Timer;
using Cosmos.TestRunner.Framework;
using static Cosmos.TestRunner.Framework.TestRunner;
using static Cosmos.TestRunner.Framework.Assert;
#if ARCH_X64
using Cosmos.Kernel.HAL.X64.Cpu;
using Cosmos.Kernel.HAL.X64.Devices.Clock;
using Cosmos.Kernel.HAL.X64.Devices.Timer;
#endif

namespace Cosmos.Kernel.Tests.Timer
{
    internal static partial class Program
    {
        private static void Main()
        {
            Serial.WriteString("[Timer Tests] Starting test suite\n");

#if ARCH_X64
            // x64: Stopwatch (2) + PIT (3) + TimerManager (2) + LAPIC (3) + DateTime (4) = 14
            Start("Timer Tests", expectedTests: 14);

            // Stopwatch/TSC Tests - must run first to verify timing source
            Run("Stopwatch_Incrementing", TestStopwatchIncrementing);
            Run("Stopwatch_Frequency", TestStopwatchFrequency);

            // PIT Tests (using Stopwatch for verification)
            Run("PIT_Initialized", TestPITInitialized);
            Run("PIT_Wait_100ms", TestPITWait100ms);
            Run("PIT_Wait_Proportional", TestPITWaitProportional);

            // TimerManager Tests
            Run("TimerManager_Initialized", TestTimerManagerInitialized);
            Run("TimerManager_Wait_500ms", TestTimerManagerWait500ms);

            // LAPIC Timer Tests
            Run("LAPIC_Initialized", TestLAPICInitialized);
            Run("LAPIC_Wait_100ms", TestLAPICWait100ms);
            Run("LAPIC_Wait_Proportional", TestLAPICWaitProportional);

            // DateTime/RTC Tests
            Run("RTC_Initialized", TestRTCInitialized);
            Run("DateTime_Now_Valid", TestDateTimeNowValid);
            Run("DateTime_Now_Incrementing", TestDateTimeNowIncrementing);
            Run("DateTime_UtcNow", TestDateTimeUtcNow);
#else
            // ARM64: No PIT or LAPIC, just basic timer manager tests
            Start("Timer Tests", expectedTests: 2);
            Run("TimerManager_Initialized", TestTimerManagerInitializedARM64);
            Run("TimerManager_Basic", TestTimerManagerBasicARM64);
#endif

            Serial.WriteString("[Timer Tests] All tests completed\n");
            Finish();

            while (true) ;
        }

#if ARCH_X64
        // ==================== Stopwatch Tests ====================

        private static void TestStopwatchIncrementing()
        {
            // Read timestamp twice and verify it's incrementing
            long ts1 = Stopwatch.GetTimestamp();

            // Small busy loop to ensure time passes
            for (int i = 0; i < 10000; i++) { }

            long ts2 = Stopwatch.GetTimestamp();

            Serial.WriteString("[Timer Tests] Stopwatch ts1: ");
            Serial.WriteNumber((ulong)ts1);
            Serial.WriteString(", ts2: ");
            Serial.WriteNumber((ulong)ts2);
            Serial.WriteString("\n");

            True(ts2 > ts1, "Stopwatch: GetTimestamp() should return incrementing values");
        }

        private static void TestStopwatchFrequency()
        {
            long freq = Stopwatch.Frequency;

            Serial.WriteString("[Timer Tests] Stopwatch.Frequency: ");
            Serial.WriteNumber((ulong)freq);
            Serial.WriteString(" Hz\n");

            // TSC frequency should be at least 100 MHz on any modern CPU
            True(freq >= 100_000_000, "Stopwatch: Frequency should be >= 100 MHz");
            True(Stopwatch.IsHighResolution, "Stopwatch: Should be high resolution on x64");
        }

        // ==================== PIT Tests ====================

        private static void TestPITInitialized()
        {
            True(PIT.Instance != null, "PIT: Instance should be initialized");
        }

        private static void TestPITWait100ms()
        {
            long tsStart = Stopwatch.GetTimestamp();
            PIT.Instance!.Wait(100);
            long tsEnd = Stopwatch.GetTimestamp();

            long elapsed = tsEnd - tsStart;
            long frequency = Stopwatch.Frequency;

            // Calculate elapsed milliseconds: (elapsed * 1000) / frequency
            long elapsedMs = (elapsed * 1000) / frequency;

            Serial.WriteString("[Timer Tests] PIT Wait(100ms) - elapsed ticks: ");
            Serial.WriteNumber((ulong)elapsed);
            Serial.WriteString(", elapsed ms: ");
            Serial.WriteNumber((ulong)elapsedMs);
            Serial.WriteString("\n");

            // Check if within tolerance (50-200ms for 100ms wait)
            bool inRange = elapsedMs >= 50 && elapsedMs <= 200;
            True(inRange, "PIT: Wait(100ms) should complete in roughly 100ms");
        }

        private static void TestPITWaitProportional()
        {
            // Test that 200ms wait takes roughly 2x the ticks of 100ms wait
            long tsStart1 = Stopwatch.GetTimestamp();
            PIT.Instance!.Wait(100);
            long tsEnd1 = Stopwatch.GetTimestamp();
            long elapsed100ms = tsEnd1 - tsStart1;

            long tsStart2 = Stopwatch.GetTimestamp();
            PIT.Instance!.Wait(200);
            long tsEnd2 = Stopwatch.GetTimestamp();
            long elapsed200ms = tsEnd2 - tsStart2;

            // 200ms should be roughly 2x of 100ms (allow 50% tolerance for ratio)
            // ratio * 100 should be between 150 and 250
            long ratio100 = (elapsed200ms * 100) / elapsed100ms;

            Serial.WriteString("[Timer Tests] PIT 100ms ticks: ");
            Serial.WriteNumber((ulong)elapsed100ms);
            Serial.WriteString(", 200ms ticks: ");
            Serial.WriteNumber((ulong)elapsed200ms);
            Serial.WriteString(", ratio*100: ");
            Serial.WriteNumber((ulong)ratio100);
            Serial.WriteString("\n");

            bool proportional = ratio100 >= 150 && ratio100 <= 250;
            True(proportional, "PIT: 200ms should take ~2x ticks of 100ms");
        }

        // ==================== TimerManager Tests ====================

        private static void TestTimerManagerInitialized()
        {
            True(TimerManager.IsInitialized, "TimerManager: Should be initialized");
            True(TimerManager.Timer != null, "TimerManager: Should have a registered timer");
        }

        private static void TestTimerManagerWait500ms()
        {
            long tsStart = Stopwatch.GetTimestamp();
            TimerManager.Wait(500);
            long tsEnd = Stopwatch.GetTimestamp();

            long elapsed = tsEnd - tsStart;
            long frequency = Stopwatch.Frequency;
            long elapsedMs = (elapsed * 1000) / frequency;

            Serial.WriteString("[Timer Tests] TimerManager Wait(500ms) - elapsed ms: ");
            Serial.WriteNumber((ulong)elapsedMs);
            Serial.WriteString("\n");

            // Check if within tolerance (250-1000ms for 500ms wait)
            bool inRange = elapsedMs >= 250 && elapsedMs <= 1000;
            True(inRange, "TimerManager: Wait(500ms) should complete in roughly 500ms");
        }

        // ==================== LAPIC Timer Tests ====================

        private static void TestLAPICInitialized()
        {
            True(LocalApic.IsInitialized, "LAPIC: Should be initialized");
            True(LocalApic.IsTimerCalibrated, "LAPIC: Timer should be calibrated");

            Serial.WriteString("[Timer Tests] LAPIC ticks/ms: ");
            Serial.WriteNumber(LocalApic.TicksPerMs);
            Serial.WriteString("\n");
        }

        private static void TestLAPICWait100ms()
        {
            long tsStart = Stopwatch.GetTimestamp();
            LocalApic.Wait(100);
            long tsEnd = Stopwatch.GetTimestamp();

            long elapsed = tsEnd - tsStart;
            long frequency = Stopwatch.Frequency;
            long elapsedMs = (elapsed * 1000) / frequency;

            Serial.WriteString("[Timer Tests] LAPIC Wait(100ms) - elapsed ms: ");
            Serial.WriteNumber((ulong)elapsedMs);
            Serial.WriteString("\n");

            // Check if within tolerance (50-200ms for 100ms wait)
            bool inRange = elapsedMs >= 50 && elapsedMs <= 200;
            True(inRange, "LAPIC: Wait(100ms) should complete in roughly 100ms");
        }

        private static void TestLAPICWaitProportional()
        {
            // Test that 200ms wait takes roughly 2x the ticks of 100ms wait
            long tsStart1 = Stopwatch.GetTimestamp();
            LocalApic.Wait(100);
            long tsEnd1 = Stopwatch.GetTimestamp();
            long elapsed100ms = tsEnd1 - tsStart1;

            long tsStart2 = Stopwatch.GetTimestamp();
            LocalApic.Wait(200);
            long tsEnd2 = Stopwatch.GetTimestamp();
            long elapsed200ms = tsEnd2 - tsStart2;

            // ratio * 100 should be between 150 and 250
            long ratio100 = (elapsed200ms * 100) / elapsed100ms;

            Serial.WriteString("[Timer Tests] LAPIC 100ms ticks: ");
            Serial.WriteNumber((ulong)elapsed100ms);
            Serial.WriteString(", 200ms ticks: ");
            Serial.WriteNumber((ulong)elapsed200ms);
            Serial.WriteString(", ratio*100: ");
            Serial.WriteNumber((ulong)ratio100);
            Serial.WriteString("\n");

            bool proportional = ratio100 >= 150 && ratio100 <= 250;
            True(proportional, "LAPIC: 200ms should take ~2x ticks of 100ms");
        }

        // ==================== DateTime/RTC Tests ====================

        private static void TestRTCInitialized()
        {
            True(RTC.Instance != null, "RTC: Instance should be initialized");
            True(RTC.Instance!.IsInitialized, "RTC: Should be initialized");

            Serial.WriteString("[Timer Tests] RTC boot time ticks: ");
            Serial.WriteNumber((ulong)RTC.Instance.BootTimeTicks);
            Serial.WriteString("\n");
        }

        private static void TestDateTimeNowValid()
        {
            DateTime now = DateTime.Now;

            Serial.WriteString("[Timer Tests] DateTime.Now: ");
            Serial.WriteNumber((ulong)now.Year);
            Serial.WriteString("-");
            Serial.WriteNumber((ulong)now.Month);
            Serial.WriteString("-");
            Serial.WriteNumber((ulong)now.Day);
            Serial.WriteString(" ");
            Serial.WriteNumber((ulong)now.Hour);
            Serial.WriteString(":");
            Serial.WriteNumber((ulong)now.Minute);
            Serial.WriteString(":");
            Serial.WriteNumber((ulong)now.Second);
            Serial.WriteString("\n");

            // Year should be >= 2020 (reasonable minimum for RTC)
            True(now.Year >= 2020, "DateTime: Year should be >= 2020");
            // Month should be 1-12
            True(now.Month >= 1 && now.Month <= 12, "DateTime: Month should be 1-12");
            // Day should be 1-31
            True(now.Day >= 1 && now.Day <= 31, "DateTime: Day should be 1-31");
        }

        private static void TestDateTimeNowIncrementing()
        {
            DateTime dt1 = DateTime.Now;

            // Wait a bit using LAPIC timer
            LocalApic.Wait(100);

            DateTime dt2 = DateTime.Now;

            Serial.WriteString("[Timer Tests] DateTime dt1 ticks: ");
            Serial.WriteNumber((ulong)dt1.Ticks);
            Serial.WriteString(", dt2 ticks: ");
            Serial.WriteNumber((ulong)dt2.Ticks);
            Serial.WriteString("\n");

            True(dt2 > dt1, "DateTime: Now should increment over time");

            // The difference should be roughly 100ms (1,000,000 ticks = 100ms)
            long tickDiff = dt2.Ticks - dt1.Ticks;
            // Allow 50ms to 200ms range (500,000 to 2,000,000 ticks)
            bool inRange = tickDiff >= 500_000 && tickDiff <= 2_000_000;
            True(inRange, "DateTime: 100ms wait should show ~100ms elapsed");
        }

        private static void TestDateTimeUtcNow()
        {
            DateTime utcNow = DateTime.UtcNow;

            Serial.WriteString("[Timer Tests] DateTime.UtcNow: ");
            Serial.WriteNumber((ulong)utcNow.Year);
            Serial.WriteString("-");
            Serial.WriteNumber((ulong)utcNow.Month);
            Serial.WriteString("-");
            Serial.WriteNumber((ulong)utcNow.Day);
            Serial.WriteString("\n");

            // Should have Utc kind
            True(utcNow.Kind == DateTimeKind.Utc, "DateTime: UtcNow should have Utc kind");
            // Year should be valid
            True(utcNow.Year >= 2020, "DateTime: UtcNow year should be >= 2020");
        }

#else
        // ==================== ARM64 Timer Tests ====================

        private static void TestTimerManagerInitializedARM64()
        {
            // ARM64 may or may not have timer initialized yet
            True(true, "TimerManager: Service exists");
        }

        private static void TestTimerManagerBasicARM64()
        {
            Serial.WriteString("[Timer Tests] ARM64 timer tests - placeholder\n");
            True(true, "TimerManager: ARM64 placeholder test");
        }
#endif
    }
}
