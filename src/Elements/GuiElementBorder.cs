using Cairo;
using Vintagestory.API.Client;

namespace PlayerModelLibUxOverhaul.Elements;

/// <summary>A coloured outline drawn just inside its bounds, such as a border around a whole dialog.</summary>
public sealed class GuiElementBorder : GuiElement
{
    private readonly double[] _color;
    private readonly double _lineWidth;
    private readonly double _radius;

    /// <param name="color">RGBA, 0-1.</param>
    /// <param name="lineWidth">Thickness in GUI units.</param>
    /// <param name="radius">Corner radius in GUI units.</param>
    public GuiElementBorder(ICoreClientAPI api, ElementBounds bounds, double[] color, double lineWidth, double radius)
        : base(api, bounds)
    {
        _color = color;
        _lineWidth = lineWidth;
        _radius = radius;
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        double lineWidth = scaled(_lineWidth);
        // Inset by half the line so the whole stroke lands inside the bounds.
        RoundRectangle(ctx, Bounds.bgDrawX + lineWidth / 2, Bounds.bgDrawY + lineWidth / 2,
            Bounds.OuterWidth - lineWidth, Bounds.OuterHeight - lineWidth, scaled(_radius));
        ctx.SetSourceRGBA(_color[0], _color[1], _color[2], _color[3]);
        ctx.LineWidth = lineWidth;
        ctx.Stroke();
    }
}
