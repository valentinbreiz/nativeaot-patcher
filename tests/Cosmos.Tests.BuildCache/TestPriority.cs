using Xunit.Sdk;
using Xunit.v3;

namespace Cosmos.Tests.BuildCache;

[AttributeUsage(AttributeTargets.Method)]
public sealed class TestPriorityAttribute : Attribute
{
    public int Priority { get; }
    public TestPriorityAttribute(int priority) => Priority = priority;
}

public sealed class PriorityOrderer : ITestCaseOrderer
{

    public IReadOnlyCollection<TTestCase> OrderTestCases<TTestCase>(IReadOnlyCollection<TTestCase> testCases) where TTestCase : notnull, ITestCase
    {
        SortedDictionary<int, List<TTestCase>> sorted = new();

        foreach (IXunitTestCase  testCase in testCases)
        {
            int priority = testCase.TestMethod.Method
                .GetCustomAttributes(typeof(TestPriorityAttribute), false)
                .Cast<TestPriorityAttribute>()
                .FirstOrDefault()
                ?.Priority ?? 0;

            if (!sorted.TryGetValue(priority, out List<TTestCase>? list))
            {
                list = new List<TTestCase>();
                sorted[priority] = list;
            }
            list.Add((TTestCase)testCase);
        }

        
        return sorted.Values.SelectMany(x => x).ToList();
    }
}
