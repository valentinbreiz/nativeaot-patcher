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

        // UART log as system-out
        if (!string.IsNullOrEmpty(results.UartLog))
        {
            writer.WriteStartElement("system-out");
            writer.WriteCData(results.UartLog);
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
        // Write XML to file
        try
        {
            var directory = Path.GetDirectoryName(_outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_outputPath, _xmlBuilder.ToString());
            Console.WriteLine($"[OutputHandlerXml] Wrote test results to: {_outputPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OutputHandlerXml] ERROR: Failed to write XML: {ex.Message}");
        }
    }
}
