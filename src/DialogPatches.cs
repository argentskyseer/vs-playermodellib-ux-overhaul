using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cairo;
using HarmonyLib;
using PlayerModelLib;
using PlayerModelLibUxOverhaul.Elements;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace PlayerModelLibUxOverhaul;

/// <summary>
/// Replaces PlayerModelLib's fixed 757x586 dialog frame with one sized from the screen.
/// Mirrors GuiDialogCreateCustomCharacter.ComposeGuis (PlayerModelLib 1.23.2) apart from the sizes.
/// </summary>
[HarmonyPatch]
public static class ComposeGuisPatch
{
    // Width at the right of the title bar for the version label, left of the menu and close buttons.
    private const double TitleBarReserved = 190;

    private const string Title = "Character Creator";

    // Window border: gold (#D4AF37), in GUI units.
    private static readonly double[] BorderColor = { 212 / 255.0, 175 / 255.0, 55 / 255.0, 1.0 };
    private const double BorderWidth = 2;
    private const double BorderRadius = 1;
    private const float TitleFontSize = 24;

    // Space above and below the title text in the title bar.
    private const double TitleBarPadding = 20;

    private static readonly FieldInfo? BeforeComposedField =
        AccessTools.Field(typeof(GuiDialogCreateCustomCharacter), "BeforeComposed");

    public static MethodBase TargetMethod() =>
        AccessTools.DeclaredMethod(typeof(GuiDialogCreateCustomCharacter), nameof(GuiDialogCreateCustomCharacter.ComposeGuis));

    public static bool Prefix(GuiDialogCreateCustomCharacter __instance)
    {
        GuiDialogCreateCustomCharacter dialog = __instance;
        ICoreClientAPI api = dialog.Api;

        (BeforeComposedField?.GetValue(null) as Action<GuiDialogCreateCustomCharacter>)?.Invoke(dialog);

        dialog.ActiveTabs.Clear();
        foreach ((string key, GuiDialogCreateCustomCharacter.ComposeTabDelegate composeTab) in dialog.Tabs)
        {
            if (dialog.TabsEnabled.TryGetValue(key, out bool enabled) && enabled)
            {
                dialog.ActiveTabs.Add(key, composeTab);
            }
        }
        if (dialog.ActiveTabs.Count == 0)
        {
            return false;
        }
        dialog.CurrentTab = Math.Clamp(dialog.CurrentTab, 0, dialog.ActiveTabs.Count - 1);

        double padding = GuiElementItemSlotGridBase.unscaledSlotPadding;
        double slotSize = GuiElementPassiveItemSlot.unscaledSlotSize;
        double yPosition = 20.0 + padding;
        double dialogPadding = GuiStyle.ElementToDialogPadding;
        (double width, double height) = Layout.ComputeContentSize(api);

        dialog.CharacterInventory = api.World.Player.InventoryManager.GetOwnInventory("character");

        // The title is at the left of the title bar, which is TitleBarPadding taller than it above and below; the
        // content moves down with it. The main tabs are centred along the bottom of the bar.
        CairoFont titleFont = CairoFont.WhiteSmallText().WithFont(GuiStyle.DecorativeFontName).WithFontSize(TitleFontSize)
            .WithOrientation(EnumTextOrientation.Left);
        double guiScale = Math.Max(0.1, RuntimeEnv.GUIScale);
        double dialogWidth = width + 2 * dialogPadding;
        double titleWidth = titleFont.GetTextExtents(Title).Width / guiScale;
        double titleHeight = Math.Ceiling(titleFont.GetFontExtents().Height / guiScale);
        double titleBarHeight = titleHeight + 2 * TitleBarPadding;
        Layout.Top = titleBarHeight - dialogPadding + Layout.GapBelowTitleBar;
        const double tabSpacing = 8;
        // Numbered steps, centred across the whole bar both ways, so they're in the middle of the window.
        ElementBounds tabBounds = ElementBounds.Fixed(0, 0, dialogWidth, titleBarHeight);
        ElementBounds backgroundBounds = ElementBounds.FixedSize(width, height).WithFixedPadding(dialogPadding);
        ElementBounds dialogBounds = ElementBounds.FixedSize(width + 2 * dialogPadding, height + 2 * dialogPadding)
            .WithAlignment(EnumDialogArea.CenterMiddle);

        int tabIndex = 0;
        GuiTab[] tabs = dialog.ActiveTabs.Select(entry => new GuiTab
        {
            Name = Lang.Get("playermodellib:tab-" + entry.Key.Replace(':', '-')),
            DataInt = tabIndex++
        }).ToArray();

        Layout.ClearAfterCompose();

        // Tabs added by other mods draw their own preview.
        string currentKey = dialog.ActiveTabs.GetAt(dialog.CurrentTab).Key;
        Layout.SeeThroughPreview = UxOverhaulModSystem.Backdrop?.Active == true && currentKey is "model" or "skin" or "class";

        GuiComposer composer = api.Gui.CreateCompo("createcharacter", dialogBounds);
        composer.AddShadedDialogBG(backgroundBounds);
        // The game's title bar always draws its text at the left, so it gets none and the title is drawn over it.
        composer.AddDialogTitleBar("", dialog.OnTitleBarClose, null, ElementStdBounds.TitleBar().WithFixedHeight(titleBarHeight));
        composer.AddStaticText(Title, titleFont, ElementBounds.Fixed(dialogPadding, TitleBarPadding, titleWidth + 4, titleHeight));
        // Gold border around the whole window, over the title bar's edge.
        composer.AddStaticElement(new GuiElementBorder(api, ElementBounds.Fixed(0, 0, width + 2 * dialogPadding, height + 2 * dialogPadding),
            BorderColor, BorderWidth, BorderRadius));
        // Mod version, small and dim, left of the title bar's menu and close buttons.
        composer.AddStaticText("v" + UxOverhaulModSystem.Version,
            CairoFont.WhiteDetailText().WithFontSize(11f).WithColor(new[] { 1.0, 1.0, 1.0, 0.45 }).WithOrientation(EnumTextOrientation.Right),
            ElementBounds.Fixed(EnumDialogArea.RightTop, -70, 9, TitleBarReserved - 70, 16));
        AssetLocation?[] icons = dialog.ActiveTabs.Select(entry => TabIcons.Find(api, TabIcons.CharacterTabs, entry.Key)).ToArray();
        composer.AddInteractiveElement(new GuiElementIconTabs(api, tabs, icons,
            CairoFont.WhiteSmallText().WithWeight(FontWeight.Bold),
            CairoFont.WhiteSmallText().WithWeight(FontWeight.Bold),
            tabBounds, dialog.OnTabClicked)
        {
            TabSpacing = tabSpacing,
            Centered = true,
            Steps = true,
            ActiveIndex = dialog.CurrentTab
        }, "tabs");
        composer.BeginChildElements(backgroundBounds);

        dialog.Composer = composer;
        dialog.Composers["createcharacter"] = composer;

        EntityBehaviorPlayerInventory? inventory = api.World.Player.Entity.GetBehavior<EntityBehaviorPlayerInventory>();
        if (inventory != null)
        {
            inventory.hideClothing = false;
        }

        dialog.ActiveTabs.GetAt(dialog.CurrentTab).Value(dialog, composer, yPosition, padding, slotSize, backgroundBounds, dialogBounds);

        try
        {
            composer.Compose();
            Layout.RunAfterCompose();
        }
        catch (Exception exception)
        {
            api.Logger.Error("[playermodellib-ux-overhaul] Failed to compose character dialog: {0}", exception);
        }

        return false;
    }
}

/// <summary>
/// Rewrites the class text ChangeClass just set with larger headings (<see cref="Descriptions.Class"/>).
/// ChangeClass also sizes the description scrollbar with the text height in screen pixels but the visible
/// height in GUI units, which only agree at 100% GUI scale, so it's re-applied with both in GUI units.
/// </summary>
[HarmonyPatch(typeof(GuiDialogCreateCustomCharacter), nameof(GuiDialogCreateCustomCharacter.ChangeClass))]
public static class ChangeClassPatch
{
    public static void Postfix(GuiDialogCreateCustomCharacter __instance)
    {
        GuiComposer? composer = __instance.Composer;
        if (composer?.GetElement("characterDesc") is not GuiElementRichtext text) return;

        PlayerSkinBehavior? skin = __instance.Api.World.Player.Entity.GetBehavior<PlayerSkinBehavior>();
        List<CharacterClass>? classes = skin == null ? null : __instance.GetAvailableClasses(__instance.CustomModelsSystem, skin.CurrentModelCode);
        if (skin != null && classes != null && __instance.CurrentClassIndex >= 0 && __instance.CurrentClassIndex < classes.Count)
        {
            text.SetNewText(Descriptions.Class(__instance, classes[__instance.CurrentClassIndex], skin.CurrentModelCode), CairoFont.WhiteDetailText());
        }
        Tabs.ScrollArea.SetScrollHeights(composer.GetElement("scrollbar") as GuiElementScrollbar, __instance.ClipHeight, text.Bounds.fixedHeight);
    }
}

/// <summary>
/// Draws the character centred in whatever preview panel the current tab set as InsetSlotBounds,
/// instead of PlayerModelLib's offsets that assume its original panel sizes.
/// </summary>
[HarmonyPatch(typeof(GuiDialogCreateCustomCharacter), nameof(GuiDialogCreateCustomCharacter.OnRenderGUI))]
public static class OnRenderGuiPatch
{
    // Vanilla seraph height in blocks; the preview is sized so this fills PreviewFill of the panel.
    private const float ReferenceHeightBlocks = 1.85f;

    // Point on the model (fraction of its height from the feet) kept at the panel centre.
    private const float FullBodyPivot = 0.5f;
    private const float SkinTabPivot = 0.6f;

    public static bool Prefix(GuiDialogCreateCustomCharacter __instance, float deltaTime, bool ___focused)
    {
        GuiDialogCreateCustomCharacter dialog = __instance;
        ICoreClientAPI api = dialog.Api;
        ElementBounds? preview = dialog.InsetSlotBounds;
        if (preview == null || dialog.ActiveTabs.Count == 0)
        {
            return false;
        }

        string key = dialog.ActiveTabs.GetAt(dialog.CurrentTab).Key;

        // Grab the world behind the preview before the dialog background covers it.
        PreviewBackdrop? backdrop = Layout.SeeThroughPreview ? UxOverhaulModSystem.Backdrop : null;
        backdrop?.Capture(preview);

        foreach (KeyValuePair<string, GuiComposer> entry in dialog.Composers)
        {
            entry.Value.Render(deltaTime);
            dialog.MouseOverCursor = entry.Value.MouseOverCursor;
        }

        backdrop?.Draw(preview);

        if (api.IsGamePaused)
        {
            api.World.Player.Entity.talkUtil.OnGameTick(deltaTime);
        }

        GuiDialogCreateCustomCharacter.RenderState = dialog.CurrentTab + 1;

        dialog.Mat.Identity();
        dialog.Mat.RotateXDeg(-14f);
        Vec4f light = dialog.Mat.TransformVector(dialog.LighPos);

        api.Render.GlPushMatrix();
        if (___focused)
        {
            api.Render.GlTranslate(0f, 0f, 150f);
        }
        api.Render.CurrentActiveShader.Uniform("lightPosition", new Vec3f(light.X, light.Y, light.Z));
        api.Render.PushScissor(preview);

        switch (key)
        {
            case "model":
            case "class":
                RenderCharacter(dialog, preview, deltaTime, 1f, FullBodyPivot, 0f);
                break;
            case "skin":
                RenderCharacter(dialog, preview, deltaTime, dialog.CharZoom * UxOverhaulModSystem.Config.SkinTabZoom, SkinTabPivot, dialog.Height);
                break;
            default:
                // Tabs added by other mods render themselves, with the same transform PlayerModelLib gives them.
                if (dialog.OnRenderIntoTab.TryGetValue(key, out Action<GuiDialogCreateCustomCharacter, float>? render))
                {
                    api.Render.GlRotate(-14f, 1f, 0f, 0f);
                    render(dialog, deltaTime);
                }
                break;
        }

        api.Render.PopScissor();
        api.Render.CurrentActiveShader.Uniform("lightPosition", new Vec3f(1f, -1f, 0f).Normalize());
        api.Render.GlPopMatrix();

        return false;
    }

    private static void RenderCharacter(GuiDialogCreateCustomCharacter dialog, ElementBounds preview, float deltaTime, float zoom, float pivot, float verticalOffset)
    {
        ICoreClientAPI api = dialog.Api;
        EntityPlayer entity = api.World.Player.Entity;

        float size = (float)(preview.OuterHeight * UxOverhaulModSystem.Config.PreviewFill / ReferenceHeightBlocks * zoom);
        float modelHeight = size * ReferenceHeightBlocks * entity.Properties.Client.Size;

        double centerX = preview.renderX + preview.OuterWidth / 2;
        double centerY = preview.renderY + preview.OuterHeight / 2 + GuiElement.scaled(verticalOffset);
        double feetY = centerY + modelHeight * pivot;

        // RenderToGui puts the feet at (posX + size, posY + 2 * size). Translate to the feet first and tilt
        // around them, so the result does not depend on where the dialog sits on screen.
        api.Render.GlPushMatrix();
        api.Render.GlTranslate((float)centerX, (float)feetY, (float)GuiElement.scaled(230.0));
        api.Render.GlRotate(-14f, 1f, 0f, 0f);
        api.Render.RenderEntityToGui(deltaTime, entity, -size, -2f * size, 0.0, dialog.Yaw, size, ColorUtil.WhiteArgb);
        api.Render.GlPopMatrix();
    }
}
