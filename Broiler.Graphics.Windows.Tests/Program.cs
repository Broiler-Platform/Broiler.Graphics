using System;
using System.Collections.Generic;

namespace Broiler.Graphics.Windows.Tests;

/// <summary>
/// Minimal console runner for the Windows/Direct2D backend tests. These exercise the CPU-side image
/// path plus a small live Direct2D command-list smoke render.
/// </summary>
internal static class Program
{
    private static int Main()
    {
        var tests = new List<(string Name, Action Body)>();
        WindowsImageTests.Register(tests);

        int passed = 0;
        var failures = new List<string>();
        Console.WriteLine($"Running {tests.Count} backend test(s)...\n");

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

        Console.WriteLine($"\n{passed}/{tests.Count} passed, {failures.Count} failed.");
        return failures.Count;
    }
}

internal sealed class AssertException(string message) : Exception(message);

internal static class Assert
{
    public static void True(bool condition, string message = "Expected true.")
    {
        if (!condition)
            throw new AssertException(message);
    }

    public static void AreEqual<T>(T expected, T actual, string? message = null)
    {
        if (!Equals(expected, actual))
            throw new AssertException(message ?? $"Expected <{expected}>, but was <{actual}>.");
    }

    public static void Throws<TException>(Action action, string? message = null) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception other)
        {
            throw new AssertException(message ?? $"Expected {typeof(TException).Name}, got {other.GetType().Name}.");
        }
        throw new AssertException(message ?? $"Expected {typeof(TException).Name}, but nothing was thrown.");
    }
}
