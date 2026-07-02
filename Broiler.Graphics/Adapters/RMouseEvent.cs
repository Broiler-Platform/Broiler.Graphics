namespace Broiler.Graphics;

public sealed class RMouseEvent(bool leftButton)
{
    public bool LeftButton => leftButton;
}