using System;
using System.Collections.Generic;

namespace Broiler.Graphics.Tests;

/// <summary>
/// A minimal console test runner. Each test is a named <see cref="Action"/>; a test "passes" if it
/// returns without throwing. Exit code is the number of failures (0 == all passed) so the process can
/// be used in CI.
/// </summary>
internal static class Program
{
    private static int Main()
    {
        var tests = new List<(string Name, Action Body)>();
        RenderListTests.Register(tests);
        LifecycleTests.Register(tests);

        int passed = 0;
        var failures = new List<string>();

        Console.WriteLine($"Running {tests.Count} test(s)...\n");

        foreach ((string name, Action body) in tests)
        {
            try
            {
                body();
                passed++;
                Console.WriteLine($"  [PASS] {name}");
            }
            catch (Exception ex)
            {
                failures.Add(name);
                Console.WriteLine($"  [FAIL] {name}");
                Console.WriteLine($"         {ex.GetType().Name}: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{passed}/{tests.Count} passed, {failures.Count} failed.");

        if (failures.Count > 0)
        {
            Console.WriteLine("\nFailed tests:");
            foreach (string name in failures)
                Console.WriteLine($"  - {name}");
        }

        return failures.Count;
    }
}
