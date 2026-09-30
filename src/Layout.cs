using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace PlayerModelLibUxOverhaul;

/// <summary>
/// Shared measurements for the character dialog. All values are unscaled GUI units, relative to the
/// dialog's background bounds (the area inside the dialog padding).
/// </summary>
public static class Layout
{
    // PlayerModelLib's original content size; we never go smaller than this.
    public const int MinContentWidth = 717;
    public const int MinContentHeight = 546;

    /// <summary>Space between the bottom of the title bar and the top of the content.</summary>
    public const double GapBelowTitleBar = 12;

    /// <summary>Top of the usable area, just below the title bar. Set when the dialog is composed, from the title bar's height.</summary>
    public static double Top { get; set; } = 23;

    public const double Gap = 12;

    /// <summary>Space between the character preview and the controls beside it.</summary>
    public const double PreviewGap = 40;

    /// <summary>Space between a control section's edges and the controls in it.</summary>
    public const double SectionPadding = 20;

    /// <summary>Space between one control section and the next.</summary>
    public const double SectionGap = 20;
    public const double ScrollbarWidth = 20;
    public const double BottomBarHeight = 34;
    public const double ArrowWidth = 35;

    private static readonly List<Action> AfterComposeActions = new();

    /// <summary>
    /// True when the dialog being composed has a see-through preview: no inset behind it, the blurred
    /// backdrop is drawn there instead.
    /// </summary>
    public static bool SeeThroughPreview { get; set; }

    public static (double width, double height) ComputeContentSize(ICoreClientAPI api)
    {
        UxOverhaulConfig config = UxOverhaulModSystem.Config;
        double scale = Math.Max(0.1, RuntimeEnv.GUIScale);
        double chrome = 2 * GuiStyle.ElementToDialogPadding;

        double screenWidth = api.Render.FrameWidth / scale;
        double screenHeight = api.Render.FrameHeight / scale;

        double width = screenWidth * config.ScreenWidthFraction;
        double height = screenHeight * config.ScreenHeightFraction;
        if (config.MaxWidth > 0) width = Math.Min(width, config.MaxWidth);
        if (config.MaxHeight > 0) height = Math.Min(height, config.MaxHeight);

        width -= chrome;
        height -= chrome;

        return (Math.Max(MinContentWidth, Math.Floor(width)), Math.Max(MinContentHeight, Math.Floor(height)));
    }

    /// <summary>Narrowest the character preview may get when the dialog is small.</summary>
    public const double MinPreviewWidth = 240;

    /// <summary>Columns don't shrink below this unless the dialog is too small to fit it.</summary>
    public const double MinColumnWidth = 230;

    // Space a column spends on its inset, scrollbar and part padding around the actual text.
    private const double ColumnChrome = 44;

    /// <summary>
    /// Width of one column of text or controls: ColumnWidthChars characters of the GUI font plus the column's
    /// own padding and scrollbar. Measured from the font so it follows GUI scale and font size.
    /// </summary>
    public static double ReadableColumnWidth()
    {
        CairoFont font = CairoFont.WhiteSmallText();
        double charWidth = font.GetTextExtents("0").XAdvance / Math.Max(0.1, RuntimeEnv.GUIScale);
        if (charWidth <= 0 || double.IsNaN(charWidth)) charWidth = 8;
        return Math.Ceiling(charWidth * UxOverhaulModSystem.Config.ColumnWidthChars + ColumnChrome);
    }

    /// <summary>
    /// The preview's own background. Left empty when the preview is see-through, since the backdrop draws
    /// over this area at render time.
    /// </summary>
    public static void AddPreviewFrame(GuiComposer composer, ElementBounds bounds, ElementBounds backgroundBounds)
    {
        if (!SeeThroughPreview)
        {
            composer.AddInset(bounds, 2);
            return;
        }

        // No element owns these bounds now, so the composer won't parent or calculate them for us.
        // PlayerModelLib still uses them for mouse hit tests and we use them for rendering.
        bounds.WithParent(backgroundBounds);
        AfterCompose(bounds.CalcWorldBounds);
    }

    /// <summary>Height, in GUI units, that rich text (VTML) takes when wrapped to the given width.</summary>
    public static double MeasureRichtext(ICoreClientAPI api, string vtml, CairoFont font, double width)
    {
        ElementBounds bounds = ElementBounds.Fixed(0, 0, width, 1).WithParent(ElementBounds.Empty);
        GuiElementRichtext text = new(api, VtmlUtil.Richtextify(api, vtml, font), bounds);
        try
        {
            text.CalcHeightAndPositions();
            return Math.Ceiling(text.TotalHeight / Math.Max(0.1, RuntimeEnv.GUIScale));
        }
        finally
        {
            text.Dispose();
        }
    }

    /// <summary>Font for descriptions shown beside controls (the model's and skin parts'): decorative, 16.</summary>
    public static CairoFont DescriptionFont() => CairoFont.WhiteDetailText().WithFont(GuiStyle.DecorativeFontName).WithFontSize(16f);

    /// <summary>Queue work that needs final bounds (scrollbar heights, text measurements).</summary>
    public static void AfterCompose(Action action) => AfterComposeActions.Add(action);

    public static void RunAfterCompose()
    {
        Action[] actions = AfterComposeActions.ToArray();
        AfterComposeActions.Clear();
        foreach (Action action in actions)
        {
            action();
        }
    }

    public static void ClearAfterCompose() => AfterComposeActions.Clear();
}
