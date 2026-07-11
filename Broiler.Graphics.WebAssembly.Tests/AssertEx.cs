using System;

namespace Broiler.Graphics.WebAssembly.Tests;

/// <summary>Thrown when an assertion fails, so the console runner can report it specially.</summary>
public sealed class AssertException : Exception
{
    public AssertException(string message) : base(message) { }
}

/// <summary>A tiny assertion helper mirroring the Broiler.Graphics core test runner.</summary>
public static class AssertEx
{
    public static void IsTrue(bool condition, string message = "Expected condition to be true.")
    {
        if (!condition)
            throw new AssertException(message);
    }

    public static void IsFalse(bool condition, string message = "Expected condition to be false.")
    {
        if (condition)
            throw new AssertException(message);
    }

    public static void AreEqual<T>(T expected, T actual, string? message = null)
    {
        if (!Equals(expected, actual))
            throw new AssertException(message ?? $"Expected <{expected}>, but was <{actual}>.");
    }

    public static void AreClose(double expected, double actual, double tolerance = 1e-9, string? message = null)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new AssertException(message ?? $"Expected <{expected}> within {tolerance}, but was <{actual}>.");
    }

    public static TException Throws<TException>(Action action, string? message = null)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException ex)
        {
            return ex;
        }
        catch (Exception other)
        {
            throw new AssertException(
                message ?? $"Expected {typeof(TException).Name}, but {other.GetType().Name} was thrown.");
        }

        throw new AssertException(message ?? $"Expected {typeof(TException).Name}, but nothing was thrown.");
    }
}
