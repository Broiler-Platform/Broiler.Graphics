namespace Broiler.Graphics.Adapters;

public sealed class BMouseEvent(bool leftButton)
{
    public bool LeftButton => leftButton;
}