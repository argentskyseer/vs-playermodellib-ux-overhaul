using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cairo;
using PlayerModelLib;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace PlayerModelLibUxOverhaul.Elements;

/// <summary>
/// Saturation/value square with vertical hue and alpha strips, a large preview and a hex field.
///
/// It subclasses PlayerModelLib's picker so the dialog's own code (randomize, presets, swatch clicks) can
/// still find it with GetColorPicker() and call SetColor(). The base class keeps the "official" colour;
/// this class keeps its own HSV copy for drawing, so hue survives when saturation or value hit zero.
/// </summary>
public class GuiElementColorPickerPlus : GuiElementColorPicker
{
    public const double DefaultHeight = 124;

    private const double StripWidth = 20;
    private const double StripGap = 8;
    private const double PanelGap = 14;
    private const double HexHeight = 28;
    private const double MinPanelWidth = 84;
    private const double MaxPanelWidth = 150;

    private enum Control { None, Square, Hue, Saturation, Value, Alpha, Hex }

    private sealed class CallbackRelay
    {
        public GuiElementColorPickerPlus? Target;
    }

    private readonly Action<double[]> _onColorChanged;
    private readonly bool _hueFree;
    private readonly bool _saturationFree;
    private readonly bool _valueFree;
    private readonly bool _alphaFree;
    private readonly float _fixedHue;
    private readonly float _fixedSaturation;
    private readonly float _fixedValue;
    private readonly float _fixedAlpha;

    private float _hue;
    private float _saturation = 1f;
    private float _value = 1f;
    private float _alpha = 1f;

    // Layout in unscaled units relative to the element, rebuilt on compose.
    private readonly List<(Control control, double x)> _strips = new();
    private double _squareSize;
    private double _panelX;
    private double _panelWidth;

    private Control _dragging = Control.None;
    private bool _pushingToBase;
    private bool _composed;

    private bool _hexFocused;
    private bool _hexSelectAll;
    private string _hexText = "";

    private LoadedTexture _texture;

    public GuiElementColorPickerPlus(ICoreClientAPI capi, ElementBounds bounds, Action<double[]> onColorChanged, ElementBounds? clipBounds,
        float fixedHue = -1f, float fixedSaturation = -1f, float fixedValue = -1f, float fixedAlpha = -1f)
        : this(capi, bounds, onColorChanged, clipBounds, fixedHue, fixedSaturation, fixedValue, fixedAlpha, new CallbackRelay())
    {
    }

    private GuiElementColorPickerPlus(ICoreClientAPI capi, ElementBounds bounds, Action<double[]> onColorChanged, ElementBounds? clipBounds,
        float fixedHue, float fixedSaturation, float fixedValue, float fixedAlpha, CallbackRelay relay)
        : base(capi, bounds, rgba => relay.Target?.OnBaseColorChanged(rgba), new double[] { 1, 1, 1, 1 }, clipBounds!,
            fixedHue < 0f ? null : fixedHue,
            fixedSaturation < 0f ? null : fixedSaturation,
            fixedValue < 0f ? null : fixedValue,
            fixedAlpha < 0f ? null : fixedAlpha)
    {
        relay.Target = this;
        _onColorChanged = onColorChanged;
        _texture = new LoadedTexture(capi);

        _hueFree = fixedHue < 0f;
        _saturationFree = fixedSaturation < 0f;
        _valueFree = fixedValue < 0f;
        _alphaFree = fixedAlpha < 0f;
        _fixedHue = fixedHue;
        _fixedSaturation = fixedSaturation;
        _fixedValue = fixedValue;
        _fixedAlpha = fixedAlpha;

        ApplyFixed();
        _hexText = FormatHex();
    }

    public override bool Focusable => true;

    #region Colour state

    private void ApplyFixed()
    {
        if (!_hueFree) _hue = _fixedHue;
        if (!_saturationFree) _saturation = _fixedSaturation;
        if (!_valueFree) _value = _fixedValue;
        if (!_alphaFree) _alpha = _fixedAlpha;
    }

    /// <summary>Called by the base class whenever its colour changes, including SetColor from PlayerModelLib.</summary>
    private void OnBaseColorChanged(double[] rgba)
    {
        if (!_pushingToBase)
        {
            RgbToHsv(rgba[0], rgba[1], rgba[2], out float h, out float s, out float v);
            // Grey or black carries no hue; keep the one the user was on.
            if (s > 0.0001f && v > 0.0001f) _hue = h;
            if (v > 0.0001f) _saturation = s;
            _value = v;
            _alpha = (float)rgba[3];
            ApplyFixed();
            if (!_hexFocused) _hexText = FormatHex();
            Redraw();
        }
        _onColorChanged(rgba);
    }

    /// <summary>Push our HSV into the base class, which notifies PlayerModelLib through the callback.</summary>
    private void Commit()
    {
        ApplyFixed();
        if (!_hexFocused) _hexText = FormatHex();
        Redraw();

        HsvToRgb(_hue, _saturation, _value, out double r, out double g, out double b);
        _pushingToBase = true;
        try
        {
            SetColor(r, g, b, _alpha);
        }
        finally
        {
            _pushingToBase = false;
        }
    }

    private string FormatHex()
    {
        HsvToRgb(_hue, _saturation, _value, out double r, out double g, out double b);
        return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", ToByte(r), ToByte(g), ToByte(b));
    }

    /// <summary>Accepts RRGGBB or AARRGGBB (the format PlayerModelLib stores), with or without '#'.</summary>
    private void TryApplyHex()
    {
        string digits = _hexText.TrimStart('#');
        if (digits.Length != 6 && digits.Length != 8) return;
        if (!uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint packed)) return;

        double alpha = digits.Length == 8 ? ((packed >> 24) & 0xFF) / 255.0 : _alpha;
        double r = ((packed >> 16) & 0xFF) / 255.0;
        double g = ((packed >> 8) & 0xFF) / 255.0;
        double b = (packed & 0xFF) / 255.0;

        RgbToHsv(r, g, b, out float h, out float s, out float v);
        if (s > 0.0001f && v > 0.0001f) _hue = h;
        _saturation = s;
        _value = v;
        _alpha = (float)alpha;
        Commit();
    }

    #endregion

    #region Layout and drawing

    private bool HasSquare => _saturationFree && _valueFree;

    private void CalcLayout()
    {
        double width = Bounds.InnerWidth / RuntimeEnv.GUIScale;
        double height = Bounds.InnerHeight / RuntimeEnv.GUIScale;

        _strips.Clear();
        double x = 0;
        if (HasSquare)
        {
            int stripCount = (_hueFree ? 1 : 0) + (_alphaFree ? 1 : 0);
            _squareSize = Math.Min(height, Math.Max(60, width - MinPanelWidth - PanelGap - stripCount * (StripWidth + StripGap)));
            x = _squareSize + StripGap;
        }
        else
        {
            _squareSize = 0;
        }

        if (_hueFree) AddStrip(Control.Hue, ref x);
        if (!HasSquare && _saturationFree) AddStrip(Control.Saturation, ref x);
        if (!HasSquare && _valueFree) AddStrip(Control.Value, ref x);
        if (_alphaFree) AddStrip(Control.Alpha, ref x);

        _panelX = x > 0 ? x - StripGap + PanelGap : 0;
        _panelWidth = Math.Clamp(width - _panelX, 10, MaxPanelWidth);
    }

    private void AddStrip(Control control, ref double x)
    {
        _strips.Add((control, x));
        x += StripWidth + StripGap;
    }

    public override void ComposeElements(Context ctxStatic, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        CalcLayout();
        _composed = true;
        Redraw();
    }

    private void Redraw()
    {
        if (!_composed) return;

        int width = Math.Max(1, (int)Bounds.InnerWidth);
        int height = Math.Max(1, (int)Bounds.InnerHeight);
        using ImageSurface surface = new(Format.Argb32, width, height);
        using Context ctx = genContext(surface);

        double h = Bounds.InnerHeight;
        HsvToRgb(_hue, _saturation, _value, out double r, out double g, out double b);

        if (HasSquare)
        {
            DrawSquare(ctx, GuiElement.scaled(_squareSize));
        }

        foreach ((Control control, double x) in _strips)
        {
            DrawStrip(ctx, control, GuiElement.scaled(x), GuiElement.scaled(StripWidth), h);
        }

        // Preview swatch and hex field on the right.
        double panelX = GuiElement.scaled(_panelX);
        double panelW = GuiElement.scaled(_panelWidth);
        double hexH = GuiElement.scaled(HexHeight);
        double previewH = h - hexH - GuiElement.scaled(6);

        DrawChecker(ctx, panelX, 0, panelW, previewH, GuiElement.scaled(6));
        ctx.SetSourceRGBA(r, g, b, _alpha);
        ctx.Rectangle(panelX, 0, panelW, previewH);
        ctx.Fill();
        EmbossRoundRectangleElement(ctx, panelX, 0, panelW, previewH, inverse: true, 2, 1);

        double hexY = h - hexH;
        ctx.SetSourceRGBA(0, 0, 0, _hexFocused ? 0.45 : 0.25);
        ctx.Rectangle(panelX, hexY, panelW, hexH);
        ctx.Fill();
        EmbossRoundRectangleElement(ctx, panelX, hexY, panelW, hexH, inverse: true, 1, 1);

        CairoFont font = CairoFont.TextInput();
        font.SetupContext(ctx);
        FontExtents extents = font.GetFontExtents();
        double textX = panelX + GuiElement.scaled(6);
        double textY = hexY + (hexH - extents.Height) / 2 + extents.Ascent;
        double textWidth = font.GetTextExtents(_hexText).XAdvance;
        if (_hexFocused && _hexSelectAll && _hexText.Length > 0)
        {
            ctx.SetSourceRGBA(0.3, 0.5, 1.0, 0.5);
            ctx.Rectangle(textX, hexY + GuiElement.scaled(4), textWidth, hexH - GuiElement.scaled(8));
            ctx.Fill();
        }
        ctx.SetSourceRGBA(1, 1, 1, _hexFocused ? 1.0 : 0.85);
        ctx.MoveTo(textX, textY);
        ctx.ShowText(_hexText);
        if (_hexFocused && !_hexSelectAll)
        {
            ctx.LineWidth = 1.5;
            ctx.MoveTo(textX + textWidth + 1, hexY + GuiElement.scaled(5));
            ctx.LineTo(textX + textWidth + 1, hexY + hexH - GuiElement.scaled(5));
            ctx.Stroke();
        }

        generateTexture(surface, ref _texture);
    }

    private void DrawSquare(Context ctx, double size)
    {
        HsvToRgb(_hue, 1f, 1f, out double hr, out double hg, out double hb);
        ctx.SetSourceRGB(hr, hg, hb);
        ctx.Rectangle(0, 0, size, size);
        ctx.Fill();

        using (LinearGradient white = new(0, 0, size, 0))
        {
            white.AddColorStop(0, new Color(1, 1, 1, 1));
            white.AddColorStop(1, new Color(1, 1, 1, 0));
            ctx.SetSource(white);
            ctx.Rectangle(0, 0, size, size);
            ctx.Fill();
        }
        using (LinearGradient black = new(0, 0, 0, size))
        {
            black.AddColorStop(0, new Color(0, 0, 0, 0));
            black.AddColorStop(1, new Color(0, 0, 0, 1));
            ctx.SetSource(black);
            ctx.Rectangle(0, 0, size, size);
            ctx.Fill();
        }
        EmbossRoundRectangleElement(ctx, 0, 0, size, size, inverse: true, 2, 1);

        // Ring marker at the current saturation/value.
        double mx = _saturation * size;
        double my = (1 - _value) * size;
        double radius = GuiElement.scaled(5);
        ctx.LineWidth = 3;
        ctx.SetSourceRGBA(0, 0, 0, 0.8);
        ctx.Arc(mx, my, radius, 0, 2 * Math.PI);
        ctx.Stroke();
        ctx.LineWidth = 1.5;
        ctx.SetSourceRGBA(1, 1, 1, 1);
        ctx.Arc(mx, my, radius, 0, 2 * Math.PI);
        ctx.Stroke();
    }

    private void DrawStrip(Context ctx, Control control, double x, double w, double h)
    {
        if (control == Control.Alpha)
        {
            DrawChecker(ctx, x, 0, w, h, GuiElement.scaled(5));
        }

        using (LinearGradient gradient = new(0, 0, 0, h))
        {
            switch (control)
            {
                case Control.Hue:
                    for (int i = 0; i <= 6; i++)
                    {
                        HsvToRgb(i / 6f, 1f, 1f, out double r, out double g, out double b);
                        gradient.AddColorStop(i / 6.0, new Color(r, g, b, 1));
                    }
                    break;
                case Control.Saturation:
                {
                    HsvToRgb(_hue, 1f, _value, out double r1, out double g1, out double b1);
                    HsvToRgb(_hue, 0f, _value, out double r0, out double g0, out double b0);
                    gradient.AddColorStop(0, new Color(r1, g1, b1, 1));
                    gradient.AddColorStop(1, new Color(r0, g0, b0, 1));
                    break;
                }
                case Control.Value:
                {
                    HsvToRgb(_hue, _saturation, 1f, out double r1, out double g1, out double b1);
                    gradient.AddColorStop(0, new Color(r1, g1, b1, 1));
                    gradient.AddColorStop(1, new Color(0, 0, 0, 1));
                    break;
                }
                case Control.Alpha:
                {
                    HsvToRgb(_hue, _saturation, _value, out double r, out double g, out double b);
                    gradient.AddColorStop(0, new Color(r, g, b, 1));
                    gradient.AddColorStop(1, new Color(r, g, b, 0));
                    break;
                }
            }
            ctx.SetSource(gradient);
            ctx.Rectangle(x, 0, w, h);
            ctx.Fill();
        }
        EmbossRoundRectangleElement(ctx, x, 0, w, h, inverse: true, 1, 1);

        // Top of the strip is the maximum, so the handle position is (1 - value).
        double y = (1 - StripValue(control)) * h;
        y = Math.Clamp(y, 2, h - 2);
        ctx.LineWidth = 4;
        ctx.SetSourceRGBA(0, 0, 0, 0.8);
        ctx.Rectangle(x - 1, y - 3, w + 2, 6);
        ctx.Stroke();
        ctx.LineWidth = 2;
        ctx.SetSourceRGBA(1, 1, 1, 1);
        ctx.Rectangle(x - 1, y - 3, w + 2, 6);
        ctx.Stroke();
    }

    private float StripValue(Control control) => control switch
    {
        Control.Hue => 1f - _hue,
        Control.Saturation => _saturation,
        Control.Value => _value,
        Control.Alpha => _alpha,
        _ => 0f
    };

    private static void DrawChecker(Context ctx, double x, double y, double w, double h, double cell)
    {
        ctx.Save();
        ctx.Rectangle(x, y, w, h);
        ctx.Clip();
        for (int row = 0; row * cell < h; row++)
        {
            for (int col = 0; col * cell < w; col++)
            {
                double shade = (row + col) % 2 == 0 ? 0.75 : 0.45;
                ctx.SetSourceRGB(shade, shade, shade);
                ctx.Rectangle(x + col * cell, y + row * cell, cell, cell);
                ctx.Fill();
            }
        }
        ctx.Restore();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (_texture.TextureId == 0) return;

        bool clip = ClipBounds != null;
        if (clip) api.Render.PushScissor(ClipBounds, stacking: true);
        api.Render.Render2DTexturePremultipliedAlpha(_texture.TextureId, (float)Bounds.renderX, (float)Bounds.renderY, (float)Bounds.InnerWidth, (float)Bounds.InnerHeight);
        if (clip) api.Render.PopScissor();
    }

    #endregion

    #region Input

    private bool IsVisibleAt(int x, int y) =>
        Bounds.PointInside(x, y) && (ClipBounds == null || ClipBounds.PointInside(x, y));

    private Control HitTest(double localX, double localY)
    {
        double x = localX / RuntimeEnv.GUIScale;
        double y = localY / RuntimeEnv.GUIScale;
        double height = Bounds.InnerHeight / RuntimeEnv.GUIScale;

        if (HasSquare && x >= 0 && x < _squareSize && y >= 0 && y < _squareSize) return Control.Square;
        foreach ((Control control, double stripX) in _strips)
        {
            if (x >= stripX - 2 && x < stripX + StripWidth + 2) return control;
        }
        if (x >= _panelX && x < _panelX + _panelWidth && y >= height - HexHeight) return Control.Hex;
        return Control.None;
    }

    public override void OnMouseDown(ICoreClientAPI api, MouseEvent mouse)
    {
        if (!IsVisibleAt(mouse.X, mouse.Y))
        {
            if (_hexFocused) SetHexFocus(false);
            return;
        }

        Control control = HitTest(mouse.X - Bounds.renderX, mouse.Y - Bounds.renderY);
        if (control == Control.Hex)
        {
            SetHexFocus(true);
            mouse.Handled = true;
            return;
        }
        if (_hexFocused) SetHexFocus(false);
        if (control == Control.None) return;

        _dragging = control;
        UpdateFromMouse(mouse.X, mouse.Y);
        mouse.Handled = true;
    }

    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        if (_dragging == Control.None) return;
        UpdateFromMouse(args.X, args.Y);
        args.Handled = true;
    }

    public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
    {
        _dragging = Control.None;
    }

    private void UpdateFromMouse(int mouseX, int mouseY)
    {
        double localX = mouseX - Bounds.renderX;
        double localY = mouseY - Bounds.renderY;

        if (_dragging == Control.Square)
        {
            double size = GuiElement.scaled(_squareSize);
            _saturation = (float)Math.Clamp(localX / size, 0, 1);
            _value = (float)Math.Clamp(1 - localY / size, 0, 1);
        }
        else
        {
            float t = (float)Math.Clamp(1 - localY / Bounds.InnerHeight, 0, 1);
            switch (_dragging)
            {
                case Control.Hue: _hue = Math.Min(1f - t, 0.9999f); break;
                case Control.Saturation: _saturation = t; break;
                case Control.Value: _value = t; break;
                case Control.Alpha: _alpha = t; break;
            }
        }
        Commit();
    }

    public override void OnFocusLost()
    {
        base.OnFocusLost();
        if (_hexFocused) SetHexFocus(false);
    }

    private void SetHexFocus(bool focused)
    {
        _hexFocused = focused;
        _hexSelectAll = focused;
        if (!focused) _hexText = FormatHex();
        Redraw();
    }

    public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
    {
        if (!_hexFocused) return;

        switch ((GlKeys)args.KeyCode)
        {
            case GlKeys.A when args.CtrlPressed:
                _hexSelectAll = true;
                break;
            case GlKeys.C when args.CtrlPressed:
                api.Forms.SetClipboardText(_hexText);
                break;
            case GlKeys.V when args.CtrlPressed:
                string pasted = new((api.Forms.GetClipboardText() ?? "").Where(IsHexChar).Take(8).ToArray());
                _hexText = "#" + pasted;
                _hexSelectAll = false;
                TryApplyHex();
                break;
            case GlKeys.BackSpace:
                _hexText = _hexSelectAll ? "#" : _hexText.Length > 1 ? _hexText[..^1] : "#";
                _hexSelectAll = false;
                break;
            case GlKeys.Enter:
            case GlKeys.KeypadEnter:
            case GlKeys.Escape:
            case GlKeys.Tab:
                TryApplyHex();
                SetHexFocus(false);
                break;
            default:
                return;
        }
        Redraw();
        args.Handled = true;
    }

    public override void OnKeyPress(ICoreClientAPI api, KeyEvent args)
    {
        if (!_hexFocused) return;

        char c = args.KeyChar;
        if (IsHexChar(c))
        {
            if (_hexSelectAll)
            {
                _hexText = "#";
                _hexSelectAll = false;
            }
            if (_hexText.TrimStart('#').Length < 8)
            {
                _hexText = "#" + _hexText.TrimStart('#') + char.ToUpperInvariant(c);
                TryApplyHex();
            }
            Redraw();
        }
        args.Handled = true;
    }

    #endregion

    public override void Dispose()
    {
        _texture?.Dispose();
        base.Dispose();
    }

    #region Colour maths

    private static bool IsHexChar(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static byte ToByte(double channel) => (byte)Math.Round(Math.Clamp(channel, 0, 1) * 255);

    private static void HsvToRgb(float h, float s, float v, out double r, out double g, out double b)
    {
        if (s <= 0f)
        {
            r = g = b = v;
            return;
        }
        float sector = (h - MathF.Floor(h)) * 6f;
        int i = (int)sector;
        float f = sector - i;
        float p = v * (1f - s);
        float q = v * (1f - s * f);
        float t = v * (1f - s * (1f - f));
        (r, g, b) = (i % 6) switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => ((double)v, (double)p, (double)q)
        };
    }

    private static void RgbToHsv(double r, double g, double b, out float h, out float s, out float v)
    {
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;
        v = (float)max;
        s = max < 1e-6 ? 0f : (float)(delta / max);
        if (delta < 1e-6)
        {
            h = 0f;
            return;
        }
        double hue = max == r ? (g - b) / delta : max == g ? 2 + (b - r) / delta : 4 + (r - g) / delta;
        hue /= 6;
        if (hue < 0) hue += 1;
        h = (float)hue;
    }

    #endregion
}
