using System.Collections;

namespace TodoWidget.Tests;

public sealed class TestFailure : Exception
{
    public TestFailure(string message) : base(message) { }
}

public static class Test
{
    public static void Assert(bool condition, string message)
    {
        if (!condition) throw new TestFailure(message);
    }

    public static void Eq<T>(T actual, T expected, string? message = null)
    {
        if (actual is IEnumerable actualSeq && expected is IEnumerable expectedSeq
            && actual is not string && expected is not string)
        {
            var a = actualSeq.Cast<object?>().ToList();
            var b = expectedSeq.Cast<object?>().ToList();
            if (!a.SequenceEqual(b))
            {
                throw new TestFailure($"{message ?? "sequence mismatch"}: expected <{string.Join(",", b)}> but was <{string.Join(",", a)}>");
            }
            return;
        }
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
            throw new TestFailure($"{message ?? "value mismatch"}: expected <{expected}> but was <{actual}>");
    }

    public static void True(bool value, string? message = null)
    {
        if (!value) throw new TestFailure(message ?? "expected true but was false");
    }

    public static void Near(double actual, double expected, double tolerance = 1e-9)
    {
        if (Math.Abs(actual - expected) > tolerance)
            throw new TestFailure($"approx mismatch: expected {expected} but was {actual}");
    }
}

public sealed class TestSuite
{
    private readonly string _name;
    private readonly IReadOnlyList<(string Name, Action Body)> _tests;

    public TestSuite(string name, IReadOnlyList<(string Name, Action Body)> tests)
    {
        _name = name;
        _tests = tests;
    }

    public int Run()
    {
        int failed = 0;
        Console.WriteLine($"== {_name} ==");
        foreach (var (name, body) in _tests)
        {
            try
            {
                body();
                Console.WriteLine($"  PASS  {name}");
            }
            catch (TestFailure f)
            {
                failed++;
                Console.WriteLine($"  FAIL  {name}: {f.Message}");
            }
            catch (Exception e)
            {
                failed++;
                Console.WriteLine($"  ERROR {name}: {e.GetType().Name}: {e.Message}");
            }
        }
        return failed;
    }
}
