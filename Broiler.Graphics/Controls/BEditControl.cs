using System;

namespace Broiler.Graphics;

/// <summary>Abstract single-line text edit control.</summary>
public abstract class BEditControl : BControl
{
    public event EventHandler? TextChanged;

    public event EventHandler? Submitted;

    protected void OnTextChanged() => TextChanged?.Invoke(this, EventArgs.Empty);

    protected void OnSubmitted() => Submitted?.Invoke(this, EventArgs.Empty);
}
