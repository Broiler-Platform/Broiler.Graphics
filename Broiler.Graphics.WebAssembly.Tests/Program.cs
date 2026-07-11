using System;
using System.Collections.Generic;

namespace Broiler.Graphics.WebAssembly.Tests;

/// <summary>
/// Console test runner for the direct-Canvas backend. Each test is a named
/// <see cref="Action"/>; it passes if it returns without throwing. The exit code is the
/// number of failures so the process is usable in CI. Mirrors the Broiler.Graphics core runner.
/// </summary>
internal static class Program
{
    private static int Main()
    {
        var tests = new List<(string Name, Action Body)>();
        CanvasTransformPolicyTests.Register(tests);
        CanvasFramePlannerTests.Register(tests);
        OracleConformanceTests.Register(tests);

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
