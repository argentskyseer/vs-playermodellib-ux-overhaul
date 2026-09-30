using System;
using System.Linq;
using Cairo;
using PlayerModelLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace PlayerModelLibUxOverhaul.Tabs;

/// <summary>
/// Model tab: group image and dropdown, model carousel (each model named under its image), then the model name beside
/// the size slider and the model's description; under that, its traits beside the classes it can pick.
/// Uses the same element keys as PlayerModelLib so its handlers (ChangeModel, OnToggleModel...) keep working.
/// </summary>
public static class ModelTab
{
    // Model image inset: 150% of PlayerModelLib's 105 (a 103 unit image plus its border). The group image
    // scales with it, since it spans the same height as a model and its name.
    private const double IconScale = 1.5;
    private const double IconBox = 105 * IconScale;
    private const double IconGap = 8;
    private const double CaptionGap = 4;
    private const double CaptionHeight = 20;
    private const double MaxCaptionFontSize = 14;
    private const double MinCaptionFontSize = 9;

    private const double SliderHeight = 20;

    // PlayerModelLib draws group images 103 wide by 142 tall; used when the image's own size can't be read.
    private const double DefaultGroupAspect = 103.0 / 142.0;

    // GuiElementListMenu's row height, which it keeps to itself.
    private static readonly System.Reflection.FieldInfo? ListLineHeight =
        HarmonyLib.AccessTools.Field(typeof(GuiElementListMenu), "unscaledLineHeight");
    private const double LoreGap = 16;

    // The available classes list; PlayerModelLib has no element for it, so these keys are ours.
    private const string ClassesKey = "modelClasses";
    private const string ClassesScrollbarKey = "modelClassesScrollbar";

    // Group dropdown: font size, room for its text padding beside the longest name, and a floor.
    private const float DropdownFontSize = 40;
    private const double DropdownTextPadding = 30;
    private const double MinDropdownWidth = 150;
    private const double MaxNameFontSize = 30;
    private const double MinNameFontSize = 20;

    public static void Compose(GuiDialogCreateCustomCharacter dialog, GuiComposer composer, double yPosition, double padding, double slotSize, ElementBounds backgroundBounds, ElementBounds dialogBounds)
    {
        ICoreClientAPI api = dialog.Api;
        CustomModelsSystem modSystem = dialog.CustomModelsSystem;
        PlayerSkinBehavior? behavior = api.World.Player.Entity.GetBehavior<PlayerSkinBehavior>();
        if (behavior == null) return;

        dialog.GetCurrentModelAndGroup(modSystem, behavior, out int modelIndex, out int groupIndex);
        dialog.GetCustomGroups(modSystem, out string[] groupValues, out string[] groupNames);
        dialog.GetCustomModels(modSystem, groupValues[groupIndex], out string[] modelValues, out string[] modelNames, out AssetLocation?[] modelIcons, out AssetLocation? groupIcon);

        // The model select controls sit SectionPadding in from the edges of their section.
        TabFrame frame = TabFrame.From(backgroundBounds);
        double x = frame.RightX + Layout.SectionPadding;
        double width = frame.RightWidth - 2 * Layout.SectionPadding;
        double y = Layout.Top + Layout.SectionPadding;

        // Character preview.
        dialog.InsetSlotBounds = ElementBounds.Fixed(0, Layout.Top, frame.PreviewWidth, frame.Height - Layout.Top);
        Layout.AddPreviewFrame(composer, dialog.InsetSlotBounds, backgroundBounds);

        // Group image, as tall as the dropdown beside it and at its own proportions.
        CairoFont dropdownFont = CairoFont.WhiteSmallishText().WithFont(GuiStyle.DecorativeFontName).WithFontSize(DropdownFontSize).WithOrientation(EnumTextOrientation.Left);
        double dropdownHeight = FontHeight(dropdownFont) + 12;
        double selectHeight = dropdownHeight + 8;
        double dropdownX = x;
        if (groupIcon != null)
        {
            double imageWidth = Math.Round(selectHeight * AspectRatio(api, groupIcon, DefaultGroupAspect));
            AddImage(composer, ElementBounds.Fixed(x, y, imageWidth, selectHeight), groupIcon, 1f, 1f);
            dropdownX += imageWidth + IconGap;
        }

        // Group dropdown in large text, just tall enough for it and wide enough for the longest group name and
        // the arrow button, which is as wide as the dropdown is tall.
        double longestGroup = groupNames.Select(name => dropdownFont.GetTextExtents(name).Width).DefaultIfEmpty(0).Max() / Math.Max(0.1, RuntimeEnv.GUIScale);
        double dropdownWidth = Math.Min(x + width - dropdownX - 8, Math.Max(MinDropdownWidth, Math.Ceiling(longestGroup + dropdownHeight + DropdownTextPadding)));
        ElementBounds groupInset = ElementBounds.Fixed(dropdownX, y, dropdownWidth + 8, selectHeight);
        ElementBounds groupDropdown = ElementBounds.Fixed(dropdownX + 4, y + 4, dropdownWidth, dropdownHeight);
        composer.AddInset(groupInset, 2);
        composer.AddScrollableDropDown(groupValues, groupNames, groupIndex,
            (code, _) => dialog.OnToggleModelGroup(code, composer),
            groupDropdown, dropdownFont, null!, "groupDropdown");
        // The open list uses the same font but PlayerModelLib's fixed 30 unit rows; make them fit the large text.
        GuiElementListMenu groupList = composer.GetScrollableDropDown("groupDropdown").listMenu;
        ListLineHeight?.SetValue(groupList, dropdownHeight);
        y += selectHeight + 14;

        // Carousel, left-aligned: [previous] < [current] > [next], at 150% of PlayerModelLib's image size, each
        // model with its name under it. The group's image is beside the dropdown above.
        AssetLocation empty = new("playermodellib", "textures/icons/empty.png");
        int count = modelValues.Length;
        int previous = count == 1 ? -1 : GameMath.Mod(modelIndex - 1, count);
        int current = count == 1 ? 0 : GameMath.Mod(modelIndex, count);
        int next = count == 1 ? -1 : GameMath.Mod(modelIndex + 1, count);

        double blockHeight = IconBox + CaptionGap + CaptionHeight;
        double cx = x;

        AddModelIcon(composer, cx, y, modelIcons, modelNames, previous, empty, 0.5f);
        cx += IconBox + IconGap;

        ElementBounds leftArrow = ElementBounds.Fixed(cx, y, Layout.ArrowWidth, IconBox - 4).WithFixedPadding(2.0);
        composer.AddIconButton("left", _ => dialog.ChangeModel(-1, composer), leftArrow);
        cx += Layout.ArrowWidth + IconGap;

        AddModelIcon(composer, cx, y, modelIcons, modelNames, current, empty, 1f);
        cx += IconBox + IconGap;

        ElementBounds rightArrow = ElementBounds.Fixed(cx, y, Layout.ArrowWidth, IconBox - 4).WithFixedPadding(2.0);
        composer.AddIconButton("right", _ => dialog.ChangeModel(1, composer), rightArrow);
        cx += Layout.ArrowWidth + IconGap;

        AddModelIcon(composer, cx, y, modelIcons, modelNames, next, empty, 0.5f);
        y += blockHeight + 16;

        // Row under the carousel, split in half: model name in large bold text, then the size slider. A long name
        // shrinks to fit its half; if it would get too small, it takes the whole row and the slider moves
        // below it. Changing model recomposes the dialog, so this is redone for every name.
        double half = Math.Floor((width - Layout.Gap) / 2);
        string modelName = modelIndex >= 0 && modelIndex < modelNames.Length ? modelNames[modelIndex] : "";
        bool sideBySide = FitFontSize(modelName, half, MaxNameFontSize, BoldTitleFont) >= MinNameFontSize;
        double nameWidth = sideBySide ? half : width;
        CairoFont nameFont = TitleFont(Math.Max(MinNameFontSize, FitFontSize(modelName, nameWidth, MaxNameFontSize, BoldTitleFont))).WithWeight(FontWeight.Bold);
        double nameHeight = FontHeight(nameFont);
        double rowHeight = sideBySide ? Math.Max(nameHeight, SliderHeight) : nameHeight;

        composer.AddDynamicText(modelName, nameFont, ElementBounds.Fixed(x, y + (rowHeight - nameHeight) / 2, nameWidth, nameHeight), "currentModelName");

        double sliderX = sideBySide ? x + half + Layout.Gap : x;
        double sliderWidth = sideBySide ? width - half - Layout.Gap : width;
        double sliderY = sideBySide ? y + (rowHeight - SliderHeight) / 2 : y + rowHeight + 12;

        float minSize = behavior.CurrentModel.SizeRange.X;
        float maxSize = behavior.CurrentModel.SizeRange.Y;
        dialog.CurrentModelSize = GameMath.Clamp(behavior.CurrentSize, minSize, maxSize);
        const double labelWidth = 130;
        composer.AddRichtext(Lang.Get("playermodellib:model-size-slider"), CairoFont.WhiteSmallText(), ElementBounds.Fixed(sliderX, sliderY, labelWidth, SliderHeight));
        composer.AddSlider(value =>
        {
            dialog.CurrentModelSize = value / 100f;
            dialog.OnModelSizeChanged();
            return true;
        }, ElementBounds.Fixed(sliderX + labelWidth, sliderY, sliderWidth - labelWidth, SliderHeight), "modelSizeSlider");
        composer.GetSlider("modelSizeSlider").SetValues((int)(dialog.CurrentModelSize * 100f), (int)(minSize * 100f), (int)(maxSize * 100f), 1, "%");
        y = sliderY + SliderHeight;

        // The model's description under its name, in the left half, as tall as its text but never more than
        // half of what's left, so the traits and classes below keep room. A longer one is cut off.
        string lore = Descriptions.ModelLore(behavior.CurrentModelCode);
        if (lore.Length > 0)
        {
            y += LoreGap;
            CairoFont loreFont = Layout.DescriptionFont();
            double loreHeight = Math.Min(Layout.MeasureRichtext(api, lore, loreFont, half), Math.Max(0, (frame.ContentBottom - y) / 2));
            composer.AddRichtext(lore, loreFont, ElementBounds.Fixed(x, y, half, loreHeight));
            y += loreHeight;
        }
        y += Layout.SectionPadding + Layout.SectionGap;

        // Under the model select, down to the confirm button: traits, then the available classes beside them.
        double textHeight = frame.ContentBottom - y;
        double descriptionWidth = Math.Floor((frame.RightWidth - Layout.SectionGap) / 2);
        ScrollArea description = new(frame.RightX, y, descriptionWidth, textHeight);
        composer.AddInset(description.Outer, 3);
        composer.BeginClip(description.Clip);
        composer.AddRichtext("", CairoFont.WhiteDetailText(), ElementBounds.Fixed(0, 0, description.InnerWidth, description.Clip.fixedHeight).WithParent(description.Clip), "modelDescription");
        composer.EndClip();
        composer.AddVerticalScrollbar(dialog.OnNewScrollbarValueModel, description.Scrollbar, "scrollbarModel");

        ScrollArea classes = new(frame.RightX + descriptionWidth + Layout.SectionGap, y, frame.RightWidth - descriptionWidth - Layout.SectionGap, textHeight);
        composer.AddInset(classes.Outer, 3);
        composer.BeginClip(classes.Clip);
        composer.AddRichtext("", CairoFont.WhiteDetailText(), ElementBounds.Fixed(0, 0, classes.InnerWidth, classes.Clip.fixedHeight).WithParent(classes.Content), ClassesKey);
        composer.EndClip();
        composer.AddVerticalScrollbar(classes.OnScroll, classes.Scrollbar, ClassesScrollbarKey);

        composer.AddSmallButton(Lang.Get("Confirm model"), dialog.OnNextImpl, frame.ConfirmButtonBounds());

        string modelCode = behavior.CurrentModelCode;
        Layout.AfterCompose(() =>
        {
            if (composer.GetRichtext("modelDescription") is GuiElementRichtext text)
            {
                text.SetNewText(Descriptions.ModelTraits(dialog, modSystem, modelCode), CairoFont.WhiteDetailText());
                ScrollArea.SetScrollHeights(composer.GetScrollbar("scrollbarModel"), description.Clip.fixedHeight, text.Bounds.fixedHeight);
            }
            if (composer.GetRichtext(ClassesKey) is GuiElementRichtext classesText)
            {
                classesText.SetNewText(Descriptions.ModelClasses(dialog, modSystem, modelCode), CairoFont.WhiteDetailText());
                ScrollArea.SetScrollHeights(composer.GetScrollbar(ClassesScrollbarKey), classes.Clip.fixedHeight, classesText.Bounds.fixedHeight);
            }
        });

        dialog.OnToggleDressOnOff(on: false);
    }

    /// <summary>Width over height of an image, or <paramref name="fallback"/> if it can't be read.</summary>
    private static double AspectRatio(ICoreClientAPI api, AssetLocation image, double fallback)
    {
        try
        {
            using ImageSurface surface = GuiElement.getImageSurfaceFromAsset(api, image);
            return surface.Height > 0 ? (double)surface.Width / surface.Height : fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    /// <summary>The game's decorative font, used for the group and model names.</summary>
    private static CairoFont TitleBaseFont() => CairoFont.WhiteMediumText().WithFont(GuiStyle.DecorativeFontName);

    private static CairoFont TitleFont(double size) =>
        TitleBaseFont().WithFontSize((float)size).WithOrientation(EnumTextOrientation.Left);

    private static CairoFont BoldTitleFont() => TitleBaseFont().WithWeight(FontWeight.Bold);

    private static double FontHeight(CairoFont font) => Math.Ceiling(font.GetFontExtents().Height / Math.Max(0.1, RuntimeEnv.GUIScale));

    /// <summary>Largest font size, up to <paramref name="maxSize"/>, at which the text fits in the given width.</summary>
    private static double FitFontSize(string text, double width, double maxSize, Func<CairoFont>? baseFont = null)
    {
        CairoFont font = (baseFont ?? TitleBaseFont)().WithFontSize((float)maxSize);
        double measured = font.GetTextExtents(text).Width / Math.Max(0.1, RuntimeEnv.GUIScale);
        // Text width grows in step with font size, so scale down by how much it overflows.
        return measured <= width ? maxSize : Math.Floor(maxSize * width / measured);
    }

    /// <summary>A model's image in its inset, with its name centred under it.</summary>
    private static void AddModelIcon(GuiComposer composer, double x, double y, AssetLocation?[] icons, string[] names, int index, AssetLocation empty, float brightness)
    {
        composer.AddInset(ElementBounds.Fixed(x, y, IconBox, IconBox), 2);
        bool hasIcon = index >= 0 && index < icons.Length && icons[index] != null;
        AddImage(composer, ElementBounds.Fixed(x + 1, y + 1, IconBox - 2, IconBox - 2), hasIcon ? icons[index]! : empty, hasIcon ? brightness : 1f, hasIcon ? 1f : 0f);

        string name = index >= 0 && index < names.Length ? names[index] : "";
        if (name.Length == 0) return;
        double fontSize = Math.Max(MinCaptionFontSize, FitFontSize(name, IconBox, MaxCaptionFontSize, CairoFont.WhiteSmallText));
        CairoFont font = CairoFont.WhiteSmallText().WithFontSize((float)fontSize).WithOrientation(EnumTextOrientation.Center);
        if (brightness < 1f) font.Color[3] = 0.6;
        composer.AddStaticText(name, font, ElementBounds.Fixed(x - IconGap / 2, y + IconBox + CaptionGap, IconBox + IconGap, CaptionHeight));
    }

    private static void AddImage(GuiComposer composer, ElementBounds bounds, AssetLocation image, float brightness, float alpha)
    {
        composer.AddExtendedImage(bounds, image, brightness, alpha, (int)GuiElement.scaled(bounds.fixedWidth), (int)GuiElement.scaled(bounds.fixedHeight));
    }
}
