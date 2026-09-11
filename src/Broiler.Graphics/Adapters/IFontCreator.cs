using Broiler.Graphics.Text;

namespace Broiler.Graphics.Adapters;

public interface IFontCreator
{
    BFont CreateFont(string family, double size, FontStyle style);
    BFont CreateFont(BFontFamily family, double size, FontStyle style);
}
