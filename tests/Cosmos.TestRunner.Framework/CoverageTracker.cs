using System.Runtime.CompilerServices;
using Cosmos.Kernel.Core.IO;

namespace Cosmos.TestRunner.Framework;

/// <summary>
/// Lightweight code coverage tracker for bare-metal kernel tests.
/// Instrumented methods call Hit(id) at entry. After tests complete,
/// Flush() sends hit data via the serial protocol.
/// </summary>
public static class CoverageTracker
{
    /// <summary>
    /// Maximum number of instrumented methods supported.
    /// Pre-allocated as a static array so ILC's --preinitstatics places it
    /// in the data segment (no runtime GC allocation).
    /// </summary>
    private const int MaxMethods = 16384;

    private static readonly byte[] _hits = new byte[MaxMethods];
    private static int _maxId;

    // Protocol constants (must match Cosmos.TestRunner.Protocol)
    private const byte CoverageData = 107;

    /// <summary>
    /// Record a method hit. Called by IL-instrumented method bodies.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Hit(int id)
    {
        if ((uint)id < MaxMethods)
        {
            _hits[id] = 1;
            // Track highest ID for efficient scanning in Flush
            if (id >= _maxId)
                _maxId = id + 1;
        }
    }

    /// <summary>
    /// Send all coverage data via serial protocol.
    /// Called by TestRunner.Finish() after tests complete.
    /// </summary>
    public static void Flush()
    {
        if (_maxId == 0)
            return;

        // Count hits
        int hitCount = 0;
        for (int i = 0; i < _maxId; i++)
        {
            if (_hits[i] != 0)
                hitCount++;
        }

        if (hitCount == 0)
            return;

        // Build payload: [HitCount:2][HitId1:2][HitId2:2]...
        int payloadSize = 2 + hitCount * 2;
        var payload = new byte[payloadSize];

        // Write HitCount (LE ushort)
        payload[0] = (byte)(hitCount & 0xFF);
        payload[1] = (byte)((hitCount >> 8) & 0xFF);

        // Write hit method IDs
        int offset = 2;
        for (int i = 0; i < _maxId; i++)
        {
            if (_hits[i] != 0)
            {
                payload[offset] = (byte)(i & 0xFF);
                payload[offset + 1] = (byte)((i >> 8) & 0xFF);
                offset += 2;
            }
        }

        SendMessage(CoverageData, payload);
    }

    /// <summary>
    /// Send a protocol message: [MAGIC:4][Command:1][Length:2][Payload:N]
    /// </summary>
    private static void SendMessage(byte command, byte[] payload)
    {
        // Magic signature (0x19740807 little-endian)
        Serial.ComWrite(0x07);
        Serial.ComWrite(0x08);
        Serial.ComWrite(0x74);
        Serial.ComWrite(0x19);

        Serial.ComWrite(command);

        ushort length = (ushort)payload.Length;
        Serial.ComWrite((byte)(length & 0xFF));
        Serial.ComWrite((byte)((length >> 8) & 0xFF));

        foreach (var b in payload)
        {
            Serial.ComWrite(b);
        }
    }
}
