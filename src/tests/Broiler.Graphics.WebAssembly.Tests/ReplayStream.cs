using System;
using System.Collections.Generic;

namespace Broiler.Graphics.WebAssembly.Tests;

/// <summary>One decoded replay op: its code and its operands.</summary>
internal sealed record ReplayOp(int Code, double[] Operands);

/// <summary>Decodes a planned <see cref="CanvasFrame"/> stream into a list of ops for assertions.</summary>
internal static class ReplayStream
{
    internal static List<ReplayOp> Parse(CanvasFrame frame)
    {
        var ops = new List<ReplayOp>();
        double[] stream = frame.Stream;
        int length = frame.StreamLength;
        int i = 0;

        while (i < length)
        {
            int code = (int)stream[i++];
            int arity = CanvasReplayOp.Arity(code);
            if (arity < 0)
                throw new AssertException($"Unknown replay op code {code} at index {i - 1}.");

            var operands = new double[arity];
            for (int k = 0; k < arity; k++)
                operands[k] = stream[i++];

            ops.Add(new ReplayOp(code, operands));
        }

        if (ops.Count != frame.OpCount)
            throw new AssertException($"Decoded {ops.Count} ops but frame reports OpCount {frame.OpCount}.");

        return ops;
    }

    /// <summary>Finds the single op with the given code, asserting there is exactly one.</summary>
    internal static ReplayOp Single(this List<ReplayOp> ops, int code)
    {
        ReplayOp? found = null;
        foreach (ReplayOp op in ops)
        {
            if (op.Code != code)
                continue;
            if (found is not null)
                throw new AssertException($"Expected exactly one op {code}, found more than one.");
            found = op;
        }

        return found ?? throw new AssertException($"Expected exactly one op {code}, found none.");
    }

    internal static int Count(this List<ReplayOp> ops, int code)
    {
        int n = 0;
        foreach (ReplayOp op in ops)
        {
            if (op.Code == code)
                n++;
        }

        return n;
    }
}
