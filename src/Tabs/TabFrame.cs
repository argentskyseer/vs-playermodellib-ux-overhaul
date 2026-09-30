using System;
using Vintagestory.API.Client;

namespace PlayerModelLibUxOverhaul.Tabs;

/// <summary>
/// Common split of every tab: character preview on the left, then two columns of text/controls. The preview
/// gets PreviewWidthFraction of the width; the columns share the rest but never grow past a readable line
/// length, any excess goes back to the preview. The split is identical on every tab so nothing jumps.
/// </summary>
internal readonly record struct TabFrame(double Width, double Height, double PreviewWidth, double RightX, double RightWidth, double ColumnWidth)
{
    public static TabFrame From(ElementBounds backgroundBounds)
    {
        double width = backgroundBounds.fixedWidth;
        double height = backgroundBounds.fixedHeight;

        double targetPreview = Math.Max(Layout.MinPreviewWidth, width * UxOverhaulModSystem.Config.PreviewWidthFraction);
        double columnWidth = Math.Floor((width - targetPreview - Layout.PreviewGap - Layout.Gap) / 2);
        columnWidth = Math.Min(columnWidth, Layout.ReadableColumnWidth());
        columnWidth = Math.Max(columnWidth, Math.Min(Layout.MinColumnWidth, Math.Floor((width - Layout.MinPreviewWidth - Layout.PreviewGap - Layout.Gap) / 2)));
        double controlsWidth = 2 * columnWidth + Layout.Gap;

        double previewWidth = width - controlsWidth - Layout.PreviewGap;
        double rightX = previewWidth + Layout.PreviewGap;
        return new TabFrame(width, height, previewWidth, rightX, controlsWidth, columnWidth);
    }

    /// <summary>Bottom of the right-hand content, above the confirm button row.</summary>
    public double ContentBottom => Height - Layout.BottomBarHeight - Layout.SectionGap;

    /// <summary>Bounds for the confirm button, bottom right like PlayerModelLib's, SectionPadding in from the right.</summary>
    public ElementBounds ConfirmButtonBounds() =>
        // Right-aligned bounds add fixedX to the right-hand position, so a negative offset moves it left.
        ElementBounds.Fixed((int)(11 - Layout.SectionPadding), (int)(Height - Layout.BottomBarHeight + 4)).WithAlignment(EnumDialogArea.RightFixed).WithFixedPadding(12.0, 6.0);
}

/// <summary>A clipped, scrollable area inside an inset. Content goes into <see cref="Content"/>.</summary>
internal sealed class ScrollArea
{
    public ElementBounds Outer { get; }
    public ElementBounds Clip { get; }
    public ElementBounds Content { get; }
    public ElementBounds Scrollbar { get; }

    public double InnerWidth => Clip.fixedWidth;

    /// <summary>Space between the inset's edge (or the scrollbar) and the content.</summary>
    public const double Padding = 20;

    public ScrollArea(double x, double y, double width, double height)
    {
        Outer = ElementBounds.Fixed(x, y, width, height);
        Clip = ElementBounds.Fixed(x + Padding, y + Padding, width - 2 * Padding - Layout.ScrollbarWidth - 4, height - 2 * Padding);
        Content = ElementBounds.Fixed(0, 0, Clip.fixedWidth, Clip.fixedHeight).WithParent(Clip);
        Scrollbar = ElementBounds.Fixed(x + width - Layout.ScrollbarWidth - 4, y + 4, Layout.ScrollbarWidth, height - 8);
    }

    public void OnScroll(float value)
    {
        Content.fixedY = -value;
        Content.CalcWorldBounds();
    }

    /// <summary>Call after compose, once the content height is known.</summary>
    public static void SetScrollHeights(GuiElementScrollbar? scrollbar, double visible, double total)
    {
        if (scrollbar == null) return;
        scrollbar.SetHeights((float)visible, (float)Math.Max(visible, total));
        scrollbar.SetScrollbarPosition(0);
    }
}
