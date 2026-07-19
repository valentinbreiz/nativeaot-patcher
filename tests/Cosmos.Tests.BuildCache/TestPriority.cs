using Xunit.Abstractions;
using Xunit.Sdk;

namespace Cosmos.Tests.BuildCache;

[AttributeUsage(AttributeTargets.Method)]
public sealed class TestPriorityAttribute : Attribute
{
    public int Priority { get; }
    public TestPriorityAttribute(int priority) => Priority = priority;
}

public sealed class PriorityOrderer : ITestCaseOrderer
{
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase
    {
        SortedDictionary<int, List<TTestCase>> sorted = new();

        foreach (TTestCase testCase in testCases)
        {
            int priority = testCase.TestMethod.Method
                .GetCustomAttributes(typeof(TestPriorityAttribute).AssemblyQualifiedName)
                .FirstOrDefault()
                ?.GetNamedArgument<int>(nameof(TestPriorityAttribute.Priority)) ?? 0;

            if (!sorted.TryGetValue(priority, out List<TTestCase>? list))
            {
                list = new List<TTestCase>();
                sorted[priority] = list;
            }
            list.Add(testCase);
        }

        foreach (List<TTestCase> list in sorted.Values)
        {
            foreach (TTestCase testCase in list)
            {
                yield return testCase;
            }
        }
    }
}
