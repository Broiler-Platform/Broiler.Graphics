using System;

namespace Broiler.Graphics;

/// <summary>Abstract push button control.</summary>
public abstract class BButtonControl : BControl
{
    public event EventHandler? Clicked;

    protected void OnClicked() => Clicked?.Invoke(this, EventArgs.Empty);
}
