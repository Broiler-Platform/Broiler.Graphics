namespace Broiler.Graphics;

public sealed class RKeyEvent(bool control, bool aKeyCode, bool cKeyCode)
{
    public bool Control => control;
    public bool AKeyCode => aKeyCode;
    public bool CKeyCode => cKeyCode;
}