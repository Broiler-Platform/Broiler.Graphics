namespace Broiler.Graphics.Adapters;

public sealed class BKeyEvent(bool control, bool aKeyCode, bool cKeyCode)
{
    public bool Control => control;
    public bool AKeyCode => aKeyCode;
    public bool CKeyCode => cKeyCode;
}