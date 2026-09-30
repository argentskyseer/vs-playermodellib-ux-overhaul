using System;
using System.Linq;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace PlayerModelLibUxOverhaul.Elements;

/// <summary>
/// A row of tabs that can show an icon before each name, as tall as the element and sized to their names.
/// Drawn like the game's own horizontal tabs, which keep their drawing private and so can't be extended, or,
/// with <see cref="Steps"/>, as numbered steps joined by lines (① Model — ② Skin — ③ Class).
/// </summary>
public sealed class GuiElementIconTabs : GuiElementTextBase
{
    private const double IconGap = 5;
    private const double IconFraction = 0.7;
    private const double InactiveAlpha = 0.5;

    // Steps: circle size, space from circle to name, and the line between steps with space either side.
    private const double StepCircleSize = 24;
    private const double StepCircleGap = 8;
    private const double StepLineLength = 40;
    private const double StepLineSpace = 5;
    private static readonly double[] StepActiveColor = { 212 / 255.0, 175 / 255.0, 55 / 255.0, 1.0 };

    private readonly GuiTab[] _tabs;
    private readonly AssetLocation?[] _icons;
    private readonly CairoFont _selectedFont;
    private readonly Action<int> _onTabClicked;

    private LoadedTexture _baseTexture;
    private readonly LoadedTexture[] _highlightTextures;
    // Steps only: the active step, drawn differently from a hovered one.
    private readonly LoadedTexture[] _activeTextures;

    // Tab positions in scaled pixels, relative to the element.
    private readonly double[] _tabX;
    private readonly double[] _tabWidth;
    private double _tabHeight;

    public int ActiveIndex;

    /// <summary>Space between tabs, in GUI units.</summary>
    public double TabSpacing = 5;

    /// <summary>Space either side of a tab's contents, in GUI units.</summary>
    public double TabPadding = 10;

    /// <summary>Centre the row of tabs in the element instead of starting at its left edge.</summary>
    public bool Centered;

    /// <summary>
    /// Draw the tabs as numbered steps joined by lines, centred vertically, instead of tab shapes. The active
    /// step's number is filled gold; the others are dimmed until hovered.
    /// </summary>
    public bool Steps;

    /// <param name="icons">One per tab, null for a tab without an icon.</param>
    /// <param name="onTabClicked">Called with the clicked tab's DataInt.</param>
    public GuiElementIconTabs(ICoreClientAPI api, GuiTab[] tabs, AssetLocation?[] icons, CairoFont font, CairoFont selectedFont, ElementBounds bounds, Action<int> onTabClicked)
        : base(api, "", font, bounds)
    {
        _tabs = tabs;
        _icons = icons;
        _selectedFont = selectedFont;
        _onTabClicked = onTabClicked;

        _baseTexture = new LoadedTexture(api);
        _highlightTextures = tabs.Select(_ => new LoadedTexture(api)).ToArray();
        _activeTextures = tabs.Select(_ => new LoadedTexture(api)).ToArray();
        _tabX = new double[tabs.Length];
        _tabWidth = new double[tabs.Length];
    }

    public override bool Focusable => enabled;

    public override void ComposeTextElements(Context ctxStatic, ImageSurface surfaceStatic)
    {
        Bounds.CalcWorldBounds();
        LayoutTabs();

        using ImageSurface surface = new(Format.Argb32, (int)Bounds.InnerWidth + 1, (int)Bounds.InnerHeight + 1);
        using Context ctx = genContext(surface);
        double[] background = GuiStyle.DialogDefaultBgColor;
        for (int i = 0; i < _tabs.Length; i++)
        {
            if (Steps)
            {
                if (i > 0) DrawStepLine(ctx, _tabX[i - 1] + _tabWidth[i - 1], _tabX[i]);
                DrawStepCircle(ctx, i, _tabX[i], filled: false, InactiveAlpha);
            }
            else
            {
                TabPath(ctx, _tabX[i], _tabWidth[i], _tabHeight);
                ctx.SetSourceRGBA(background[0], background[1], background[2], background[3] * 0.75);
                ctx.FillPreserve();
                ShadePath(ctx);
            }
            DrawContents(ctx, surface, i, _tabX[i], Font, InactiveAlpha);
        }
        generateTexture(surface, ref _baseTexture);

        for (int i = 0; i < _tabs.Length; i++)
        {
            if (Steps)
            {
                ComposeStep(i, active: false, ref _highlightTextures[i]);
                ComposeStep(i, active: true, ref _activeTextures[i]);
            }
            else
            {
                ComposeHighlight(i);
            }
        }
    }

    /// <summary>A step drawn over the base texture: brighter when hovered, number filled gold when active.</summary>
    private void ComposeStep(int index, bool active, ref LoadedTexture texture)
    {
        using ImageSurface surface = new(Format.Argb32, (int)_tabWidth[index] + 1, (int)_tabHeight + 1);
        using Context ctx = genContext(surface);
        DrawStepCircle(ctx, index, 0, filled: active, 1);
        DrawContents(ctx, surface, index, 0, active ? _selectedFont : Font, 1);
        generateTexture(surface, ref texture);
    }

    /// <summary>The numbered circle at the start of a step, centred vertically.</summary>
    private void DrawStepCircle(Context ctx, int index, double x, bool filled, double alpha)
    {
        double size = scaled(StepCircleSize);
        double radius = size / 2;
        double centerX = x + radius;
        double centerY = _tabHeight / 2;

        ctx.NewPath();
        ctx.Arc(centerX, centerY, radius - scaled(1), 0, GameMath.TWOPI);
        if (filled)
        {
            ctx.SetSourceRGBA(StepActiveColor[0], StepActiveColor[1], StepActiveColor[2], StepActiveColor[3] * alpha);
            ctx.Fill();
        }
        else
        {
            ctx.SetSourceRGBA(1, 1, 1, alpha);
            ctx.LineWidth = scaled(1.5);
            ctx.Stroke();
        }

        // The step number, centred in the circle; dark on the gold fill.
        CairoFont font = CairoFont.WhiteSmallText().WithWeight(FontWeight.Bold);
        font.Color = filled ? new[] { 0.1, 0.08, 0.05, alpha } : new[] { 1.0, 1.0, 1.0, alpha };
        font.SetupContext(ctx);
        string number = (index + 1).ToString();
        TextExtents extents = ctx.TextExtents(number);
        ctx.MoveTo(centerX - extents.Width / 2 - extents.XBearing, centerY - extents.Height / 2 - extents.YBearing);
        ctx.ShowText(number);
    }

    /// <summary>The line joining one step to the next, StepLineSpace clear of each.</summary>
    private void DrawStepLine(Context ctx, double fromX, double toX)
    {
        double y = Math.Round(_tabHeight / 2) + 0.5;
        ctx.NewPath();
        ctx.MoveTo(fromX + scaled(StepLineSpace), y);
        ctx.LineTo(toX - scaled(StepLineSpace), y);
        ctx.SetSourceRGBA(1, 1, 1, InactiveAlpha);
        ctx.LineWidth = scaled(1);
        ctx.Stroke();
    }

    private void LayoutTabs()
    {
        // Steps are joined by a line instead of spaced like tabs, and have no padding of their own.
        double spacing = Steps ? scaled(2 * StepLineSpace + StepLineLength) : scaled(TabSpacing);
        double padding = Steps ? 0 : scaled(TabPadding);
        _tabHeight = Bounds.InnerHeight;

        double x = Steps ? 0 : spacing;
        for (int i = 0; i < _tabs.Length; i++)
        {
            _tabX[i] = x;
            _tabWidth[i] = Math.Ceiling(ContentWidth(i) + 2 * padding + 1);
            x += _tabWidth[i] + spacing;
        }

        if (Centered && _tabs.Length > 0)
        {
            double used = _tabX[^1] + _tabWidth[^1] - _tabX[0];
            double shift = Math.Max(0, Math.Floor((Bounds.InnerWidth - used) / 2)) - _tabX[0];
            for (int i = 0; i < _tabs.Length; i++) _tabX[i] += shift;
        }
    }

    // Steps sit in a taller element, so their icons follow the circle rather than the element's height.
    private double IconSize => Math.Round(Steps ? scaled(StepCircleSize) : _tabHeight * IconFraction);

    private double ContentWidth(int index)
    {
        double text = Math.Max(Font.GetTextExtents(_tabs[index].Name).Width, _selectedFont.GetTextExtents(_tabs[index].Name).Width);
        return text + (_icons[index] != null ? IconSize + scaled(IconGap) : 0) + StepLead;
    }

    /// <summary>Width of a step's numbered circle and the space after it; nothing for plain tabs.</summary>
    private double StepLead => Steps ? scaled(StepCircleSize + StepCircleGap) : 0;

    /// <summary>The raised look of the active or hovered tab, drawn over the base texture.</summary>
    private void ComposeHighlight(int index)
    {
        double width = _tabWidth[index];
        using ImageSurface surface = new(Format.Argb32, (int)width + 1, (int)_tabHeight + 1);
        using Context ctx = genContext(surface);
        double[] background = GuiStyle.DialogDefaultBgColor;

        TabPath(ctx, 0, width, _tabHeight);
        ctx.SetSourceRGBA(background[0], background[1], background[2], background[3]);
        ctx.FillPreserve();
        ctx.SetSourceRGBA(background[0] * 1.6, background[1] * 1.6, background[2] * 1.6, 1);
        ctx.LineWidth = 3.5;
        ctx.StrokePreserve();
        SurfaceTransformBlur.BlurPartial(surface, 5.2, 10);
        ctx.SetSourceRGBA(background[0], background[1], background[2], background[3]);
        ctx.LineWidth = 1;
        ctx.StrokePreserve();
        ShadePath(ctx);

        DrawContents(ctx, surface, index, 0, _selectedFont, 1);
        generateTexture(surface, ref _highlightTextures[index]);
    }

    /// <summary>Tab outline, rounded at the top and open at the bottom where it joins its content.</summary>
    private void TabPath(Context ctx, double x, double width, double height)
    {
        double radius = scaled(2);
        ctx.NewPath();
        ctx.MoveTo(x, height);
        ctx.LineTo(x, radius);
        ctx.Arc(x + radius, radius, radius, GameMath.PI, 1.5 * GameMath.PI);
        ctx.LineTo(x + width - radius, 0);
        ctx.Arc(x + width - radius, radius, radius, 1.5 * GameMath.PI, GameMath.TWOPI);
        ctx.LineTo(x + width, height);
        ctx.ClosePath();
    }

    private void DrawContents(Context ctx, ImageSurface surface, int index, double x, CairoFont font, double alpha)
    {
        double left = x + (Steps ? StepLead : scaled(TabPadding));
        AssetLocation? icon = _icons[index];
        if (icon != null)
        {
            double size = IconSize;
            DrawIcon(ctx, surface, icon, left, (_tabHeight - size) / 2, size, alpha);
            left += size + scaled(IconGap);
        }

        double previousAlpha = font.Color[3];
        font.Color[3] = previousAlpha * alpha;
        font.SetupContext(ctx);
        double textHeight = font.GetFontExtents().Height;
        DrawTextLineAt(ctx, _tabs[index].Name, left, (_tabHeight - textHeight) / 2);
        font.Color[3] = previousAlpha;
    }

    private void DrawIcon(Context ctx, ImageSurface surface, AssetLocation icon, double x, double y, double size, double alpha)
    {
        try
        {
            if (icon.Path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                IAsset? asset = api.Assets.TryGet(icon);
                if (asset == null) return;
                // SVGs are drawn straight into the surface, tinted white like the game's own icons. The surface
                // holds premultiplied colour, so a dimmed white is grey at the same alpha.
                int shade = (int)(alpha * 255);
                surface.Flush();
                api.Gui.DrawSvg(asset, surface, (int)x, (int)y, (int)size, (int)size, ColorUtil.ToRgba(shade, shade, shade, shade));
                surface.MarkDirty();
                return;
            }

            using ImageSurface image = getImageSurfaceFromAsset(api, icon);
            ctx.Save();
            ctx.Translate(x, y);
            ctx.Scale(size / image.Width, size / image.Height);
            ctx.SetSourceSurface(image, 0, 0);
            ctx.PaintWithAlpha(alpha);
            ctx.Restore();
        }
        catch (Exception exception)
        {
            api.Logger.Warning("[playermodellib-ux-overhaul] Could not draw tab icon {0}: {1}", icon, exception.Message);
        }
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        api.Render.Render2DTexture(_baseTexture.TextureId, (int)Bounds.renderX, (int)Bounds.renderY, (int)Bounds.InnerWidth + 1, (int)Bounds.InnerHeight + 1);

        int hovered = TabAtMouse();
        for (int i = 0; i < _tabs.Length; i++)
        {
            if (i != ActiveIndex && i != hovered) continue;
            LoadedTexture texture = Steps && i == ActiveIndex ? _activeTextures[i] : _highlightTextures[i];
            api.Render.Render2DTexturePremultipliedAlpha(texture.TextureId,
                (int)(Bounds.renderX + _tabX[i]), (int)Bounds.renderY, (int)_tabWidth[i] + 1, (int)_tabHeight + 1);
        }
    }

    private int TabAtMouse()
    {
        double x = api.Input.MouseX - Bounds.absX;
        double y = api.Input.MouseY - Bounds.absY;
        for (int i = 0; i < _tabs.Length; i++)
        {
            if (x >= _tabX[i] && x < _tabX[i] + _tabWidth[i] && y >= 0 && y < _tabHeight) return i;
        }
        return -1;
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseDownOnElement(api, args);
        int index = TabAtMouse();
        if (index < 0) return;
        args.Handled = true;
        SetValue(index);
    }

    public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
    {
        if (!HasFocus || _tabs.Length == 0) return;
        if (args.KeyCode == (int)GlKeys.Right)
        {
            SetValue((ActiveIndex + 1) % _tabs.Length);
            args.Handled = true;
        }
        else if (args.KeyCode == (int)GlKeys.Left)
        {
            SetValue(GameMath.Mod(ActiveIndex - 1, _tabs.Length));
            args.Handled = true;
        }
    }

    public void SetValue(int index, bool callHandler = true)
    {
        ActiveIndex = index;
        if (!callHandler) return;
        api.Gui.PlaySound("menubutton_wood");
        _onTabClicked(_tabs[index].DataInt);
    }

    public override void Dispose()
    {
        base.Dispose();
        _baseTexture.Dispose();
        foreach (LoadedTexture texture in _highlightTextures.Concat(_activeTextures))
        {
            texture.Dispose();
        }
    }
}
