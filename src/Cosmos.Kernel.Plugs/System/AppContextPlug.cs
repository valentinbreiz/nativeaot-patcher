using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using System.Text.Unicode;
using Cosmos.Build.API.Attributes;
using Cosmos.Kernel.Core.Utilities;
using Cosmos.Kernel.Graphics;
using Cosmos.Kernel.Core.IO;

namespace Cosmos.Kernel.Plugs.System;

[Plug(typeof(AppContext))]
public static partial class AppContextPlug
{
    [LibraryImport("*", EntryPoint = "RhGetKnobValues")]
    private static unsafe partial uint RhGetKnobValues(out byte** keys, out byte** values);
    private static SimpleDictionary<string, object?>? dataStore;
    private static SimpleDictionary<string, bool>? switches;

    [PlugMember]
    public static void EnsureInitialized()
    {
        if (dataStore is not null) return;
        unsafe
        {
            uint count = RhGetKnobValues(out byte** knobKeys, out byte** knobValues);

            dataStore = new(capacity: (int)count);
            switches = new();

            for (int i = 0; i < count; i++)
            {
                byte* ptrKey = knobKeys[i];
                byte* ptrVal = knobValues[i];

                string key = Utf8Decode(new(ptrKey, Strlen(ptrKey)));
                string value = Utf8Decode(new(ptrVal, Strlen(ptrVal)));

                Serial.WriteString(key);
                Serial.WriteString(" = ");
                Serial.WriteString(value + "\n");

                dataStore[key] = value;

                if (bool.TryParse(value, out bool result))
                {
                    switches.Add(key, result);
                }
            }
        }
    }



    [PlugMember]
    public static bool TryGetSwitch(string switchName, out bool isEnabled)
    {
        EnsureInitialized();

        ArgumentException.ThrowIfNullOrEmpty(switchName);

        if (switches != null)
        {
            if (switches.TryGetValue(switchName, out isEnabled))
                return true;
        }

        var data = GetData(switchName);

        if (GetData(switchName) is string value && bool.TryParse(value, out isEnabled))
        {
            return true;
        }

        isEnabled = false;
        return false;
    }

    [PlugMember]
    public static object? GetData(string name)
    {
        EnsureInitialized();
        Serial.WriteString("Getting Data!\n");

        dataStore!.TryGetValue(name, out object? data);
        Serial.WriteString((string)data);
        return data;
    }
    [PlugMember]
    public static void SetData(string switchName, object? data)
    {
        EnsureInitialized();

        dataStore![switchName] = data;
    }

    [PlugMember]
    public static void SetSwitch(string switchName, bool isEnabled)
    {
        EnsureInitialized();

        dataStore![switchName] = isEnabled;
    }


    internal unsafe static int Strlen(byte* str, int max = int.MaxValue)
    {
        int length = 0;
        while (str[length] != 0x00 && length < max)
        {
            length++;
        }
        return length;
    }

    internal static unsafe string Utf8Decode(ReadOnlySpan<byte> bytes)
    {
        // WORKAROUND: Utf8.ToUtf16() has a NativeAOT ARM64 codegen bug with infinite loop
        // Use simple ASCII-only conversion instead (AppContext knob values are ASCII-only)
        Span<char> buffer = stackalloc char[bytes.Length];

        fixed (byte* pBytes = bytes)
        fixed (char* pChars = buffer)
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = pBytes[i];
                if (b > 127)
                {
                    // Non-ASCII - should not happen for AppContext knobs
                    return string.Empty;
                }
                pChars[i] = (char)b;
            }
        }

        return new string(buffer);
    }
}
