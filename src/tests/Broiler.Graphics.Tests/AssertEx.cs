using System;

namespace Broiler.Graphics.Tests;

/// <summary>Thrown when an assertion fails. Kept distinct so the runner can report it specially.</summary>
public sealed class AssertException : Exception
{
    public AssertException(string message) : base(message) { }
}

/// <summary>
/// A tiny assertion helper. No external test framework: assertions throw <see cref="AssertException"/>
/// which the console runner catches and reports.
/// </summary>
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

    public static void AreNotEqual<T>(T notExpected, T actual, string? message = null)
    {
        if (Equals(notExpected, actual))
            throw new AssertException(message ?? $"Did not expect <{actual}>.");
    }

    public static void IsInstanceOf<T>(object? value, string? message = null)
    {
        if (value is not T)
            throw new AssertException(message ?? $"Expected instance of {typeof(T).Name}, but was <{value?.GetType().Name ?? "null"}>.");
    }

    /// <summary>Asserts that <paramref name="action"/> throws <typeparamref name="TException"/>.</summary>
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
