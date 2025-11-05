using System;
using System.IO;
using System.Text;
using System.Xml;

namespace Cosmos.TestRunner.Engine.OutputHandlers;

/// <summary>
/// XML output handler producing JUnit-compatible XML for CI integration
/// </summary>
public class OutputHandlerXml : OutputHandlerBase
{
    private readonly string _outputPath;
    private readonly StringBuilder _xmlBuilder;
    private string _suiteName = string.Empty;
    private string _architecture = string.Empty;
    private DateTime _suiteStartTime;

    public OutputHandlerXml(string outputPath)
    {
        _outputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
        _xmlBuilder = new StringBuilder();
    }

    public override void OnTestSuiteStart(string suiteName, string architecture)
    {
        _suiteName = suiteName;
        _architecture = architecture;
        _suiteStartTime = DateTime.Now;

        // Clear any previous content
        _xmlBuilder.Clear();
    }

    public override void OnTestStart(int testNumber, string testName)
    {
        // Test starts are tracked but not written to XML directly
    }

    public override void OnTestPass(int testNumber, string testName, uint durationMs)
    {
        // Test passes are recorded in OnTestSuiteEnd
    }

    public override void OnTestFail(int testNumber, string testName, string errorMessage, uint durationMs)
    {
        // Test failures are recorded in OnTestSuiteEnd
    }

    public override void OnTestSkip(int testNumber, string testName, string reason)
    {
        // Skipped tests are recorded in OnTestSuiteEnd
    }

    public override void OnTestSuiteEnd(TestResults results)
    {
        // Generate JUnit XML format
        // Note: StringWriter uses UTF-16 internally, which affects the XML declaration
        // We fix this in Complete() by replacing utf-16 with utf-8 before writing to file
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = Encoding.UTF8,
            OmitXmlDeclaration = false
        };

        using var stringWriter = new StringWriter(_xmlBuilder);
        using var writer = XmlWriter.Create(stringWriter, settings);

        writer.WriteStartDocument();

        // <testsuites> root element
        writer.WriteStartElement("testsuites");
        writer.WriteAttributeString("name", _suiteName);
        writer.WriteAttributeString("tests", results.TotalTests.ToString());
        writer.WriteAttributeString("failures", results.FailedTests.ToString());
        writer.WriteAttributeString("skipped", results.SkippedTests.ToString());
        writer.WriteAttributeString("time", results.TotalDuration.TotalSeconds.ToString("F3"));
        writer.WriteAttributeString("timestamp", _suiteStartTime.ToString("yyyy-MM-ddTHH:mm:ss"));

        // <testsuite> element
        writer.WriteStartElement("testsuite");
        writer.WriteAttributeString("name", _suiteName);
        writer.WriteAttributeString("tests", results.TotalTests.ToString());
        writer.WriteAttributeString("failures", results.FailedTests.ToString());
        writer.WriteAttributeString("skipped", results.SkippedTests.ToString());
        writer.WriteAttributeString("time", results.TotalDuration.TotalSeconds.ToString("F3"));
        writer.WriteAttributeString("timestamp", _suiteStartTime.ToString("yyyy-MM-ddTHH:mm:ss"));

        // Add architecture as a property
        writer.WriteStartElement("properties");
        writer.WriteStartElement("property");
        writer.WriteAttributeString("name", "architecture");
        writer.WriteAttributeString("value", _architecture);
        writer.WriteEndElement(); // property

        if (results.TimedOut)
        {
            writer.WriteStartElement("property");
            writer.WriteAttributeString("name", "timedOut");
            writer.WriteAttributeString("value", "true");
            writer.WriteEndElement(); // property
        }

        writer.WriteEndElement(); // properties

        // Individual test cases
        foreach (var test in results.Tests)
        {
            writer.WriteStartElement("testcase");
            writer.WriteAttributeString("name", test.TestName);
            writer.WriteAttributeString("classname", _suiteName);
            writer.WriteAttributeString("time", (test.DurationMs / 1000.0).ToString("F3"));

            if (test.Status == TestStatus.Failed)
            {
                writer.WriteStartElement("failure");
                writer.WriteAttributeString("message", test.ErrorMessage);
                writer.WriteAttributeString("type", "TestFailure");
                writer.WriteString(test.ErrorMessage);
                writer.WriteEndElement(); // failure
            }
            else if (test.Status == TestStatus.Skipped)
            {
                writer.WriteStartElement("skipped");
                writer.WriteAttributeString("message", test.ErrorMessage);
                writer.WriteEndElement(); // skipped
            }

            writer.WriteEndElement(); // testcase
        }

        // Suite-level error if any
        if (!string.IsNullOrEmpty(results.ErrorMessage))
        {
            writer.WriteStartElement("system-err");
            writer.WriteCData(results.ErrorMessage);
            writer.WriteEndElement(); // system-err
        }

        // UART log as system-out (filter invalid XML characters)
        if (!string.IsNullOrEmpty(results.UartLog))
        {
            writer.WriteStartElement("system-out");
            writer.WriteCData(FilterInvalidXmlChars(results.UartLog));
            writer.WriteEndElement(); // system-out
        }

        writer.WriteEndElement(); // testsuite
        writer.WriteEndElement(); // testsuites

        writer.WriteEndDocument();
    }

    public override void OnError(string errorMessage)
    {
        // Errors are captured in TestResults and written in OnTestSuiteEnd
    }

    public override void Complete()
    {
        // Write XML to file with explicit UTF-8 encoding
        try
        {
            var directory = Path.GetDirectoryName(_outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // StringWriter uses UTF-16 internally, so XmlWriter writes encoding="utf-16"
            // We need to replace it with "utf-8" since we're writing to file as UTF-8
            var xmlContent = _xmlBuilder.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"");

            // Write with UTF-8 encoding (no BOM) to match the XML declaration
            File.WriteAllText(_outputPath, xmlContent, new UTF8Encoding(false));
            Console.WriteLine($"[OutputHandlerXml] Wrote test results to: {_outputPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OutputHandlerXml] ERROR: Failed to write XML: {ex.Message}");
        }
    }

    /// <summary>
    /// Filters characters that are invalid in XML 1.0.
    /// Valid characters are: #x9 | #xA | #xD | [#x20-#xD7FF] | [#xE000-#xFFFD] | [#x10000-#x10FFFF]
    /// </summary>
    private static string FilterInvalidXmlChars(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var filtered = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            // Allow: tab (0x09), newline (0x0A), carriage return (0x0D), and standard printable characters
            if (c == 0x09 || c == 0x0A || c == 0x0D ||
                (c >= 0x20 && c <= 0xD7FF) ||
                (c >= 0xE000 && c <= 0xFFFD))
            {
                filtered.Append(c);
            }
            // Skip invalid characters
        }
        return filtered.ToString();
    }
}
