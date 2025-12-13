using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace Cosmos.TestRunner.Engine.Hosts;

/// <summary>
/// Hardware host for Raspberry Pi 4B via Cosmos-RPi-Dev-Board PCB.
///
/// Communication flow:
/// 1. Send ISO to PCB controller (via HTTP API or serial)
/// 2. PCB flashes ISO to SD card
/// 3. PCB starts TFTP server and boots RPi
/// 4. PCB collects UART log from RPi
/// 5. PCB returns UART log to this host
///
/// The PCB controller can be accessed via:
/// - HTTP API (when using the board's ESP32 WiFi)
/// - Serial port (when directly connected to development machine)
/// </summary>
public class RaspberryPiHost : IHardwareHost
{
    public string Architecture => "arm64";
    public string HardwareName => "Raspberry Pi 4B";

    private readonly string _controllerEndpoint;
    private readonly string? _serialPort;
    private readonly int _baudRate;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Create a RaspberryPiHost that communicates via HTTP API
    /// </summary>
    /// <param name="controllerEndpoint">HTTP endpoint of the PCB controller (e.g., "http://192.168.1.100:8080")</param>
    public RaspberryPiHost(string controllerEndpoint)
    {
        _controllerEndpoint = controllerEndpoint.TrimEnd('/');
        _serialPort = null;
        _baudRate = 0;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10) // Long timeout for ISO upload
        };
    }

    /// <summary>
    /// Create a RaspberryPiHost that communicates via serial port
    /// </summary>
    /// <param name="serialPort">Serial port (e.g., "/dev/ttyUSB0" or "COM3")</param>
    /// <param name="baudRate">Baud rate (default 115200)</param>
    public RaspberryPiHost(string serialPort, int baudRate = 115200)
    {
        _controllerEndpoint = string.Empty;
        _serialPort = serialPort;
        _baudRate = baudRate;
        _httpClient = new HttpClient();
    }

    /// <summary>
    /// Check if the hardware test board is available
    /// </summary>
    public async Task<bool> IsAvailableAsync()
    {
        if (!string.IsNullOrEmpty(_serialPort))
        {
            return File.Exists(_serialPort) ||
                   (OperatingSystem.IsWindows() && _serialPort.StartsWith("COM"));
        }

        try
        {
            var response = await _httpClient.GetAsync($"{_controllerEndpoint}/status");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Send ISO to PCB and run tests on Raspberry Pi
    /// </summary>
    public async Task<HardwareRunResult> RunKernelAsync(string isoPath, string uartLogPath, int timeoutSeconds = 120)
    {
        if (!File.Exists(isoPath))
        {
            return new HardwareRunResult
            {
                Success = false,
                ErrorMessage = $"ISO file not found: {isoPath}"
            };
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!string.IsNullOrEmpty(_serialPort))
            {
                return await RunViaSerialAsync(isoPath, uartLogPath, timeoutSeconds, stopwatch);
            }
            else
            {
                return await RunViaHttpAsync(isoPath, uartLogPath, timeoutSeconds, stopwatch);
            }
        }
        catch (Exception ex)
        {
            return new HardwareRunResult
            {
                Success = false,
                ErrorMessage = $"Hardware test failed: {ex.Message}",
                TimedOut = ex is TaskCanceledException or OperationCanceledException
            };
        }
    }

    private async Task<HardwareRunResult> RunViaHttpAsync(string isoPath, string uartLogPath, int timeoutSeconds, Stopwatch stopwatch)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

        // Step 1: Upload ISO to PCB
        Console.WriteLine($"[RaspberryPiHost] Uploading ISO to {_controllerEndpoint}...");
        var isoBytes = await File.ReadAllBytesAsync(isoPath, cts.Token);

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(isoBytes), "iso", Path.GetFileName(isoPath));

        var uploadResponse = await _httpClient.PostAsync($"{_controllerEndpoint}/upload", content, cts.Token);
        if (!uploadResponse.IsSuccessStatusCode)
        {
            var error = await uploadResponse.Content.ReadAsStringAsync(cts.Token);
            return new HardwareRunResult
            {
                Success = false,
                ErrorMessage = $"Failed to upload ISO: {error}"
            };
        }

        var bootTime = stopwatch.Elapsed;
        Console.WriteLine($"[RaspberryPiHost] ISO uploaded in {bootTime.TotalSeconds:F1}s");

        // Step 2: Start test execution
        Console.WriteLine("[RaspberryPiHost] Starting test execution...");
        var startResponse = await _httpClient.PostAsync($"{_controllerEndpoint}/run", null, cts.Token);
        if (!startResponse.IsSuccessStatusCode)
        {
            var error = await startResponse.Content.ReadAsStringAsync(cts.Token);
            return new HardwareRunResult
            {
                Success = false,
                ErrorMessage = $"Failed to start test: {error}",
                BootTime = bootTime
            };
        }

        // Step 3: Poll for completion
        Console.WriteLine("[RaspberryPiHost] Waiting for test completion...");
        string uartLog = string.Empty;
        bool completed = false;

        while (!cts.Token.IsCancellationRequested)
        {
            await Task.Delay(500, cts.Token);

            var statusResponse = await _httpClient.GetAsync($"{_controllerEndpoint}/status", cts.Token);
            if (statusResponse.IsSuccessStatusCode)
            {
                var status = await statusResponse.Content.ReadFromJsonAsync<TestBoardStatus>(cancellationToken: cts.Token);
                if (status?.State == "completed" || status?.State == "error")
                {
                    completed = status.State == "completed";

                    // Get UART log
                    var logResponse = await _httpClient.GetAsync($"{_controllerEndpoint}/uart-log", cts.Token);
                    if (logResponse.IsSuccessStatusCode)
                    {
                        uartLog = await logResponse.Content.ReadAsStringAsync(cts.Token);
                    }
                    break;
                }
            }
        }

        var testTime = stopwatch.Elapsed - bootTime;

        // Save UART log to file
        if (!string.IsNullOrEmpty(uartLog))
        {
            var logDir = Path.GetDirectoryName(uartLogPath);
            if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }
            await File.WriteAllTextAsync(uartLogPath, uartLog);
        }

        return new HardwareRunResult
        {
            Success = completed,
            UartLog = uartLog,
            TimedOut = !completed && cts.Token.IsCancellationRequested,
            BootTime = bootTime,
            TestTime = testTime
        };
    }

    private async Task<HardwareRunResult> RunViaSerialAsync(string isoPath, string uartLogPath, int timeoutSeconds, Stopwatch stopwatch)
    {
        // Serial protocol for PCB communication
        // Commands:
        //   UPLOAD <size>\n<binary data>  - Upload ISO
        //   RUN\n                          - Start test
        //   STATUS\n                       - Get status
        //   LOG\n                          - Get UART log

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

        // For now, use a Python helper script for serial communication
        // This avoids adding System.IO.Ports dependency and handles platform differences
        var helperScript = Path.Combine(
            Path.GetDirectoryName(typeof(RaspberryPiHost).Assembly.Location) ?? ".",
            "rpi-test-helper.py"
        );

        if (!File.Exists(helperScript))
        {
            return new HardwareRunResult
            {
                Success = false,
                ErrorMessage = $"Serial helper script not found: {helperScript}. Use HTTP mode or install the helper."
            };
        }

        var psi = new ProcessStartInfo
        {
            FileName = "python3",
            Arguments = $"\"{helperScript}\" --port \"{_serialPort}\" --baud {_baudRate} " +
                       $"--iso \"{isoPath}\" --log \"{uartLogPath}\" --timeout {timeoutSeconds}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            return new HardwareRunResult
            {
                Success = false,
                ErrorMessage = "Failed to start serial helper process"
            };
        }

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return new HardwareRunResult
            {
                Success = false,
                TimedOut = true,
                ErrorMessage = $"Test timed out after {timeoutSeconds}s"
            };
        }

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();

        string uartLog = string.Empty;
        if (File.Exists(uartLogPath))
        {
            uartLog = await File.ReadAllTextAsync(uartLogPath);
        }

        return new HardwareRunResult
        {
            Success = process.ExitCode == 0,
            UartLog = uartLog,
            ErrorMessage = process.ExitCode != 0 ? stderr : string.Empty,
            BootTime = stopwatch.Elapsed,
            TestTime = TimeSpan.Zero
        };
    }

    private record TestBoardStatus
    {
        public string State { get; init; } = "unknown";
        public string Message { get; init; } = string.Empty;
        public int Progress { get; init; }
    }
}
