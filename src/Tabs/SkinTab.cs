using System;
using System.Collections.Generic;
using System.Linq;
using PlayerModelLib;
using PlayerModelLibUxOverhaul.Elements;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace PlayerModelLibUxOverhaul.Tabs;

/// <summary>
/// Skin tab: preview with its toggles on the left, skin-part tabs and two smoothly scrolling columns on
/// the right, file buttons along the bottom. Part widgets keep PlayerModelLib's keys ("dropdown-X",
/// "picker-X", "colorpicker-X", "canvas-X") so Randomize, presets and import keep updating them.
/// </summary>
public static class SkinTab
{
    private const string PresetsTab = "presets";
    private const double TitleHeight = 24;
    private const double SwatchSize = 22;
    private const double SwatchStep = SwatchSize + 5;
    private const double ButtonHeight = 30;
    private const double PresetButtonHeight = 40;
    private const double PartTabsHeight = 36;

    // A part with a guiDescription: space from its controls to the divider line, and from the line to the text.
    private const double DividerGap = 10;
    private const double DescriptionPadding = 40;

    // Space above and below the line between parts.
    private const double SeparatorSpace = 10;

    public static void Compose(GuiDialogCreateCustomCharacter dialog, GuiComposer composer, double yPosition, double padding, double slotSize, ElementBounds backgroundBounds, ElementBounds dialogBounds)
    {
        ICoreClientAPI api = dialog.Api;
        PlayerSkinBehavior? behavior = api.World.Player.Entity.GetBehavior<PlayerSkinBehavior>();
        if (behavior == null) return;

        SkinnablePartExtended[] parts = behavior.AvailableSkinParts.Get().OfType<SkinnablePartExtended>().Where(part => !part.HideFromGui).ToArray();
        Dictionary<string, Dictionary<string, string>> presets = behavior.CurrentModel.Presets;
        dialog.Presets = presets;

        List<string> tabCodes = new();
        if (presets.Count > 0) tabCodes.Add(PresetsTab);
        tabCodes.AddRange(parts.Select(part => part.TabCode).Distinct());
        dialog.CurrentSkinPartTab = Math.Clamp(dialog.CurrentSkinPartTab, 0, Math.Max(0, tabCodes.Count - 1));
        string currentTab = tabCodes.Count > 0 ? tabCodes[dialog.CurrentSkinPartTab] : "";

        (api.World.Player.Entity.Properties.Client.Renderer as EntityShapeRenderer)?.TesselateShape();
        EntityBehaviorPlayerInventory? inventory = api.World.Player.Entity.GetBehavior<EntityBehaviorPlayerInventory>();
        if (inventory != null) inventory.hideClothing = dialog.HideClothing;

        TabFrame frame = TabFrame.From(backgroundBounds);

        ComposeLeftPanel(dialog, composer, frame, backgroundBounds);
        ComposeBottomBar(dialog, composer, frame, dialogBounds);

        double areaTop = Layout.Top;
        if (tabCodes.Count > 1)
        {
            GuiTab[] tabs = tabCodes.Select((code, index) => new GuiTab
            {
                Name = Lang.Get("playermodellib:skinpart-tab-" + code),
                DataInt = index
            }).ToArray();
            AssetLocation?[] icons = tabCodes.Select(code => TabIcons.Find(api, TabIcons.SkinPartTabs, code)).ToArray();
            ElementBounds tabBounds = ElementBounds.Fixed(frame.RightX + Layout.SectionPadding, Layout.Top + Layout.SectionPadding,
                frame.RightWidth - 2 * Layout.SectionPadding, PartTabsHeight);
            GuiElementIconTabs partTabs = new(api, tabs, icons,
                CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold),
                CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold),
                tabBounds, dialog.OnSkinPartTabClicked)
            {
                ActiveIndex = dialog.CurrentSkinPartTab
            };
            composer.AddInteractiveElement(partTabs, "skinpart-tabs");
            areaTop += Layout.SectionPadding + PartTabsHeight + Layout.SectionGap;
        }
        double areaHeight = frame.ContentBottom - areaTop;

        if (currentTab == PresetsTab)
        {
            ComposePresets(dialog, composer, behavior, frame.RightX, frame.RightWidth, areaTop, areaHeight, padding);
        }
        else
        {
            SkinnablePartExtended[] tabParts = parts.Where(part => part.TabCode == currentTab).ToArray();
            ComposePartColumns(dialog, composer, behavior, tabParts, frame.RightX, frame.RightWidth, areaTop, areaHeight, padding);
        }

        composer.GetToggleButton("showdressedtoggle").SetValue(dialog.HideClothing);
        dialog.OnToggleDressOnOff(dialog.HideClothing);
    }

    private static void ComposeLeftPanel(GuiDialogCreateCustomCharacter dialog, GuiComposer composer, TabFrame frame, ElementBounds backgroundBounds)
    {
        bool cpmButton = PlayerModelModSystem.Settings.ExportCPMModelButton;
        double width = frame.PreviewWidth;
        // Preview, then a section of buttons under it with SectionPadding around them.
        int rows = cpmButton ? 3 : 2;
        double buttonsHeight = rows * ButtonHeight + (rows - 1) * 6;
        double previewHeight = frame.Height - Layout.Top - Layout.SectionGap - 2 * Layout.SectionPadding - buttonsHeight;

        dialog.InsetSlotBounds = ElementBounds.Fixed(0, Layout.Top, width, previewHeight);
        Layout.AddPreviewFrame(composer, dialog.InsetSlotBounds, backgroundBounds);

        CairoFont buttonFont = CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center);
        double x = Layout.SectionPadding;
        double buttonsWidth = width - 2 * Layout.SectionPadding;
        double y = Layout.Top + previewHeight + Layout.SectionGap + Layout.SectionPadding;

        composer.AddToggleButton(Lang.Get("playermodellib:gui-button-hide-clothing"), CairoFont.WhiteSmallText(), dialog.OnToggleDressOnOff,
            ElementBounds.Fixed(x, y, buttonsWidth, ButtonHeight), "showdressedtoggle");
        y += ButtonHeight + 6;

        double half = (buttonsWidth - 6) / 2;
        composer.AddButton(Lang.Get("Randomize"), () => dialog.OnRandomizeSkin(new Dictionary<string, string>()),
            ElementBounds.Fixed(x, y, half, ButtonHeight), buttonFont, EnumButtonStyle.Small);
        composer.AddButton(Lang.Get("Last selection"), () => dialog.OnRandomizeSkin(dialog.GetPreviousSelection()),
            ElementBounds.Fixed(x + half + 6, y, half, ButtonHeight), buttonFont, EnumButtonStyle.Small);
        y += ButtonHeight + 6;

        if (cpmButton)
        {
            composer.AddButton(Lang.Get("Export as CPM model"), dialog.OnExportAsCPMModel,
                ElementBounds.Fixed(x, y, buttonsWidth, ButtonHeight), buttonFont, EnumButtonStyle.Small);
        }
    }

    private static void ComposeBottomBar(GuiDialogCreateCustomCharacter dialog, GuiComposer composer, TabFrame frame, ElementBounds dialogBounds)
    {
        CairoFont font = CairoFont.WhiteSmallText();
        double y = frame.Height - Layout.BottomBarHeight + 4;

        List<string> skins = dialog.GetExistingSkins();
        if (skins.Count == 0) skins.Add("");

        // Save/load group, left to right after the preview column.
        ElementBounds export = ElementBounds.Fixed((int)(frame.RightX + Layout.SectionPadding), (int)y).WithFixedPadding(8.0, 6.0);
        ElementBounds import = ElementBounds.Fixed(0, (int)y).WithFixedPadding(8.0, 6.0).RightOf(export, 6);
        ElementBounds dropdown = ElementBounds.Fixed(0, y, Math.Clamp(frame.RightWidth * 0.25, 140, 240), 26).RightOf(import, 6);
        ElementBounds folder = ElementBounds.Fixed(0, (int)y).WithFixedPadding(8.0, 6.0).RightOf(dropdown, 6);
        ElementBounds refresh = ElementBounds.Fixed(0, (int)y).WithFixedPadding(8.0, 6.0).RightOf(folder, 6);

        composer.AddButton(Lang.Get("Export"), dialog.OnExport, export, font, EnumButtonStyle.Small);
        composer.AddButton(Lang.Get("Import"), dialog.OnImport, import, font, EnumButtonStyle.Small);
        composer.AddScrollableDropDown(skins.ToArray(), skins.ToArray(), 0, (code, _) => dialog.OnToggleSkinImport(code), dropdown, dialogBounds, "dropdown-import-skin");
        composer.AddButton(Lang.Get("Folder"), dialog.OpenSkinsFolder, folder, font, EnumButtonStyle.Small);
        composer.AddButton(Lang.Get("Refresh"), dialog.OnRefresh, refresh, font, EnumButtonStyle.Small);

        composer.AddSmallButton(Lang.Get("Confirm Skin"), dialog.OnNextImpl, frame.ConfirmButtonBounds());
    }

    private static void ComposePresets(GuiDialogCreateCustomCharacter dialog, GuiComposer composer, PlayerSkinBehavior behavior, double x, double width, double top, double height, double padding)
    {
        string modelCode = behavior.CurrentModelCode.Replace(':', '-');
        ScrollArea area = new(x, top, width, height);
        composer.AddInset(area.Outer, 2, 0.9f);

        composer.BeginClip(area.Clip);
        ElementBounds previous = ElementBounds.Fixed(0, 0);
        ElementBounds buttonBounds = ElementBounds.Fixed(2, 0).WithFixedWidth(area.InnerWidth - 4).WithFixedHeight(PresetButtonHeight);
        double total = 0;
        foreach (string presetCode in dialog.Presets.Keys)
        {
            previous = dialog.ComposePresetButton(presetCode, modelCode, composer, previous, buttonBounds, area.Content, padding);
            total = previous.fixedY + previous.fixedHeight + padding;
        }
        composer.EndClip();

        AddColumnScrollbar(composer, area, "skinparts-presets-scrollbar", total);
    }

    private static void ComposePartColumns(GuiDialogCreateCustomCharacter dialog, GuiComposer composer, PlayerSkinBehavior behavior, SkinnablePartExtended[] parts, double x, double width, double top, double height, double padding)
    {
        int? split = SplitIndex(dialog.Api, behavior.CurrentModelCode, parts);
        if (split == null)
        {
            ScrollArea single = new(x, top, width, height);
            ComposeColumn(dialog, composer, behavior, parts, single, single.InnerWidth, padding, "skinparts-left-scrollbar");
            return;
        }

        double columnWidth = Math.Floor((width - Layout.SectionGap) / 2);
        ScrollArea left = new(x, top, columnWidth, height);
        ScrollArea right = new(x + width - columnWidth, top, columnWidth, height);
        double partWidth = left.InnerWidth;

        ComposeColumn(dialog, composer, behavior, parts.Take(split.Value), left, partWidth, padding, "skinparts-left-scrollbar");
        ComposeColumn(dialog, composer, behavior, parts.Skip(split.Value), right, partWidth, padding, "skinparts-right-scrollbar");
    }

    private static void ComposeColumn(GuiDialogCreateCustomCharacter dialog, GuiComposer composer, PlayerSkinBehavior behavior, IEnumerable<SkinnablePartExtended> parts, ScrollArea area, double partWidth, double padding, string scrollbarKey)
    {
        composer.AddInset(area.Outer, 4, 0.9f);

        composer.BeginClip(area.Clip);
        ElementBounds previous = ElementBounds.Fixed(0, 0);
        double total = 0;
        bool first = true;
        foreach (SkinnablePartExtended part in parts)
        {
            // A line between parts, with SeparatorSpace above and below it. Scrollable elements, like
            // everything else in the column, so they move with it.
            if (!first)
            {
                composer.AddScrollableInset(ElementBounds.Fixed(0, total + SeparatorSpace - 1, partWidth, 2).WithParent(area.Content), 2, 1f);
                previous = ElementBounds.Fixed(0, total, partWidth, 2 * SeparatorSpace);
            }
            first = false;

            // A part with a guiDescription gets the left half for its controls and the description beside them;
            // the row is as tall as whichever is taller, so the next part goes below both.
            string? description = SkinPartExtras.Description(dialog.Api, behavior.CurrentModelCode, part.Code);
            double controlsWidth = description == null ? partWidth : Math.Floor((partWidth - DividerGap) / 2);
            ElementBounds controls = ComposePart(dialog, composer, behavior, part, previous, area, controlsWidth, padding);
            ElementBounds row = controls;
            if (description != null)
            {
                // Divider line DividerGap after the controls, then the description DescriptionPadding after the
                // line, centred vertically in the row.
                double dividerX = controlsWidth + DividerGap;
                double textX = dividerX + DescriptionPadding;
                double textWidth = partWidth - textX - padding;
                CairoFont font = Layout.DescriptionFont();
                double textHeight = Layout.MeasureRichtext(dialog.Api, description, font, textWidth);
                double rowHeight = Math.Max(controls.fixedHeight, textHeight + 2 * padding);
                composer.AddRichtext(description, font,
                    ElementBounds.Fixed(textX, controls.fixedY + (rowHeight - textHeight) / 2, textWidth, textHeight).WithParent(area.Content));
                composer.AddScrollableInset(ElementBounds.Fixed(dividerX - 1, controls.fixedY + padding, 2, rowHeight - 2 * padding)
                    .WithParent(area.Content), 2, 1f);
                row = ElementBounds.Fixed(0, controls.fixedY, partWidth, rowHeight).WithParent(area.Content);
            }
            composer.AddScrollableInset(row, 0, 1f);
            total = row.fixedY + row.fixedHeight;
        }
        composer.EndClip();

        AddColumnScrollbar(composer, area, scrollbarKey, total);
    }

    private static void AddColumnScrollbar(GuiComposer composer, ScrollArea area, string key, double contentHeight)
    {
        composer.AddExtendedVerticalScrollbar(area.OnScroll, area.Scrollbar, key, area.Outer);
        Layout.AfterCompose(() =>
        {
            area.Content.CalcWorldBounds();
            GuiElemenExtendedtScrollbar? scrollbar = composer.GetElement(key) as GuiElemenExtendedtScrollbar;
            if (scrollbar == null) return;
            scrollbar.SetHeights((float)area.Clip.fixedHeight, (float)Math.Max(area.Clip.fixedHeight, contentHeight + 4));
            scrollbar.SetScrollbarPosition(0);
        });
    }

    #region Parts

    private enum PartKind { ColorPicker, Canvas, Swatches, DropDown }

    private static PartKind KindOf(SkinnablePartExtended part)
    {
        if (part.Type == EnumSkinnableType.Texture && part.SolidColor) return PartKind.ColorPicker;
        if (part.Type == EnumSkinnableType.Texture && part.Canvas) return PartKind.Canvas;
        if (part.Type == EnumSkinnableType.Texture && !part.UseDropDown) return PartKind.Swatches;
        return PartKind.DropDown;
    }

    private static SkinnablePartVariant[] StandardVariants(SkinnablePart part) =>
        part.Variants.Where(variant => variant.Category is "standard" or null or "").ToArray();

    private static double CanvasHeight(double partWidth) => Math.Clamp(Math.Floor(partWidth * 0.8), 256, 360);

    /// <summary>Swatches per row for a given width, and the maxLineWidth that makes the helper wrap there.</summary>
    private static (int perRow, int maxLineWidth) SwatchLayout(double width)
    {
        int perRow = Math.Max(1, (int)Math.Floor((width + 5) / SwatchStep));
        return (perRow, (int)((perRow - 1) * SwatchStep));
    }

    private static double ColorPartHeight(int swatchRows, double padding) =>
        padding + TitleHeight + 2 * padding + GuiElementColorPickerPlus.DefaultHeight + 3 * padding + swatchRows * SwatchStep + padding;

    /// <summary>
    /// Where the right column starts: after a part whose Colbreak is also allowed by allowColBreak in its
    /// model config (see <see cref="SkinPartExtras"/>). Null when there is no such part, so the tab uses one
    /// wide column.
    /// </summary>
    private static int? SplitIndex(ICoreClientAPI api, string modelCode, SkinnablePartExtended[] parts)
    {
        int colbreak = Array.FindIndex(parts, part => part.Colbreak && SkinPartExtras.AllowColBreak(api, modelCode, part.Code));
        return colbreak >= 0 ? colbreak + 1 : null;
    }

    private static ElementBounds ComposePart(GuiDialogCreateCustomCharacter dialog, GuiComposer composer, PlayerSkinBehavior behavior, SkinnablePartExtended part, ElementBounds previous, ScrollArea area, double partWidth, double padding)
    {
        ElementBounds titleBounds = ElementBounds.Fixed(0, 0).WithFixedSize(partWidth, 30.0);
        switch (KindOf(part))
        {
            case PartKind.ColorPicker:
                return ComposeColorPart(dialog, composer, behavior, part, previous, area, partWidth, padding);
            case PartKind.Canvas:
                return ComposeCanvasPart(dialog, composer, behavior, part, previous, area, partWidth, padding);
            case PartKind.Swatches:
                return dialog.ComposeSwatchSkinPart(part, composer, previous, titleBounds,
                    ElementBounds.Fixed(0, 0).WithFixedSize(SwatchSize, SwatchSize), area.Content, behavior, padding, area.Clip);
            default:
                return dialog.ComposeDropDownSkinPart(part, composer, previous, titleBounds,
                    ElementBounds.Fixed(0, 0).WithFixedSize(partWidth, 24.0 + padding * 2.0), area.Content, behavior, padding, area.Clip);
        }
    }

    private static void AddPartTitle(GuiComposer composer, string partCode, ElementBounds bounds)
    {
        composer.AddRichtext(Lang.Get("skinpart-" + partCode), CairoFont.WhiteSmallText(), bounds);
        string? description = Lang.GetIfExists("skinpartdesc-" + partCode);
        if (description != null)
        {
            composer.AddHoverText(description, CairoFont.WhiteSmallText(), 300, bounds.FlatCopy());
        }
    }

    private static ElementBounds ComposeColorPart(GuiDialogCreateCustomCharacter dialog, GuiComposer composer, PlayerSkinBehavior behavior, SkinnablePartExtended part, ElementBounds previous, ScrollArea area, double partWidth, double padding)
    {
        string partCode = part.Code;
        AppliedSkinnablePartVariant? applied = behavior.AppliedSkinParts.Get().FirstOrDefault(variant => variant.PartCode == partCode);
        SkinnablePartVariant[] variants = StandardVariants(part);
        int[] colors = variants.Select(variant => variant.Color).ToArray();
        int selectedIndex = Math.Max(0, Array.FindIndex(variants, variant => variant.Code == applied?.Code));

        double innerWidth = partWidth - 4 * padding;
        (int perRow, int maxLineWidth) = SwatchLayout(innerWidth);
        int rows = colors.Length == 0 ? 0 : (colors.Length + perRow - 1) / perRow;

        ElementBounds container = ElementBounds.Fixed(0, 0).WithFixedSize(partWidth, ColorPartHeight(rows, padding)).WithParent(area.Content).FixedUnder(previous);

        double y = padding;
        ElementBounds title = ElementBounds.Fixed(2 * padding, y, innerWidth, TitleHeight).WithParent(container);
        y += TitleHeight + 2 * padding;
        ElementBounds pickerBounds = ElementBounds.Fixed(2 * padding, y, innerWidth, GuiElementColorPickerPlus.DefaultHeight).WithParent(container);
        y += GuiElementColorPickerPlus.DefaultHeight + 3 * padding;
        ElementBounds swatchStart = ElementBounds.Fixed(2 * padding, y, SwatchSize, SwatchSize).WithParent(container);

        AddPartTitle(composer, partCode, title);

        GuiElementColorPickerPlus picker = new(dialog.Api, pickerBounds, rgba => dialog.OnToggleSkinPartActuallyColor(partCode, rgba), area.Clip,
            part.FixedHue, part.FixedSaturation, part.FixedLuminosity, part.FixedAlpha);
        composer.AddInteractiveElement(picker, "colorpicker-" + partCode);
        double[] argb = GuiDialogCreateCustomCharacter.HexArgbToDoubleArray(applied?.Code ?? "#FFFFFFFF");
        picker.SetColor(argb[1], argb[2], argb[3], argb[0]);

        if (colors.Length > 0)
        {
            composer.AddScrollableColorListPicker(colors, index => dialog.OnTogglePickerPartColor(partCode, index), swatchStart, maxLineWidth, area.Clip, "picker-" + partCode);
            AddSwatchTooltips(composer, partCode, variants);
            composer.ScrollableColorListPickerSetValue("picker-" + partCode, selectedIndex);
        }

        return container;
    }

    private static void AddSwatchTooltips(GuiComposer composer, string partCode, SkinnablePartVariant[] variants)
    {
        for (int i = 0; i < variants.Length; i++)
        {
            GuiElementScrollableColorListPicker? swatch = composer.GetScrollableColorListPicker("picker-" + partCode + "-" + i);
            if (swatch == null) continue;
            swatch.ShowToolTip = true;
            string key = "skinpart-" + partCode + "-" + variants[i].Code;
            swatch.TooltipText = Lang.HasTranslation(key) ? Lang.Get(key) : Lang.Get("color-" + variants[i].Code);
        }
    }

    private static ElementBounds ComposeCanvasPart(GuiDialogCreateCustomCharacter dialog, GuiComposer composer, PlayerSkinBehavior behavior, SkinnablePartExtended part, ElementBounds previous, ScrollArea area, double partWidth, double padding)
    {
        string partCode = part.Code;
        AppliedSkinnablePartVariant? applied = behavior.AppliedSkinParts.Get().FirstOrDefault(variant => variant.PartCode == partCode);
        SkinnablePartVariant[] variants = StandardVariants(part);
        double canvasHeight = CanvasHeight(partWidth);

        ElementBounds container = ElementBounds.Fixed(0, 0).WithFixedSize(partWidth, 30 + padding + canvasHeight + padding).WithParent(area.Content).FixedUnder(previous);
        ElementBounds title = ElementBounds.Fixed(padding, padding, partWidth - 2 * padding, 30 - 2 * padding).WithParent(container);
        ElementBounds canvas = ElementBounds.Fixed(padding, 30 + padding, partWidth - 2 * padding, canvasHeight - 2 * padding).WithParent(container);

        AddPartTitle(composer, partCode, title);

        // Same data handling as PlayerModelLib's ComposeCanvasSkinPart, but passing the column clip.
        int colorCount = Math.Clamp(part.ColorsNumber, 1, 14);
        TextureCanvasData data = new()
        {
            Width = part.Size[0],
            Height = part.Size[1],
            Colors = Enumerable.Repeat(-1, colorCount + 1).ToArray(),
            Pixels = new int[part.Size[0] * part.Size[1]]
        };
        string? stored = applied?.Code ?? (variants.Length > 0 ? variants[0].Code : null);
        if (stored != null)
        {
            TextureCanvasData loaded = TextureCanvasData.Deserialize(stored);
            if (loaded.Width == data.Width && loaded.Height == data.Height && loaded.Colors.Length == data.Colors.Length)
            {
                data = loaded;
            }
        }
        data.Colors = data.Colors[1..];

        composer.AddCanvasEditor(data, canvas, changed => dialog.OnToggleSkinPartCanvas(partCode, changed), area.Clip, "canvas-" + partCode);
        return container;
    }

    #endregion
}
