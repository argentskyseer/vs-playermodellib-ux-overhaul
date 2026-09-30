using PlayerModelLib;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace PlayerModelLibUxOverhaul.Tabs;

/// <summary>
/// Class tab: class name with arrows, and the class background and traits under it, in one column.
/// Keeps PlayerModelLib's element keys ("className", "characterDesc", "scrollbar") because ChangeClass writes
/// the name and the combined background and traits text to them.
/// </summary>
public static class ClassTab
{
    public static void Compose(GuiDialogCreateCustomCharacter dialog, GuiComposer composer, double yPosition, double padding, double slotSize, ElementBounds backgroundBounds, ElementBounds dialogBounds)
    {
        ICoreClientAPI api = dialog.Api;
        (api.World.Player.Entity.Properties.Client.Renderer as EntityShapeRenderer)?.TesselateShape();

        TabFrame frame = TabFrame.From(backgroundBounds);
        // The class name row sits SectionPadding in from the edges of its section.
        double x = frame.RightX + Layout.SectionPadding;
        double width = frame.RightWidth - 2 * Layout.SectionPadding;
        double y = Layout.Top + Layout.SectionPadding;
        double rowHeight = slotSize;

        dialog.InsetSlotBounds = ElementBounds.Fixed(0, Layout.Top, frame.PreviewWidth, frame.Height - Layout.Top);
        Layout.AddPreviewFrame(composer, dialog.InsetSlotBounds, backgroundBounds);

        // [<] [ class name ] [>]
        ElementBounds leftArrow = ElementBounds.Fixed(x, y, Layout.ArrowWidth, rowHeight - 4).WithFixedPadding(2.0);
        ElementBounds rightArrow = ElementBounds.Fixed(x + width - Layout.ArrowWidth - 4, y, Layout.ArrowWidth, rowHeight - 4).WithFixedPadding(2.0);
        double nameX = x + Layout.ArrowWidth + 4 + Layout.Gap;
        double nameWidth = width - 2 * (Layout.ArrowWidth + 4 + Layout.Gap);
        ElementBounds nameInset = ElementBounds.Fixed(nameX, y, nameWidth, rowHeight);

        CairoFont nameFont = CairoFont.WhiteMediumText().WithFont(GuiStyle.DecorativeFontName).WithWeight(Cairo.FontWeight.Bold);
        double fontHeight = nameFont.GetFontExtents().Height / RuntimeEnv.GUIScale;
        ElementBounds nameText = ElementBounds.Fixed(nameX + 4, y + (rowHeight - fontHeight) / 2, nameWidth - 8, fontHeight);

        composer.AddIconButton("left", _ => dialog.ChangeClass(-1), leftArrow);
        composer.AddInset(nameInset, 2);
        composer.AddDynamicText("", nameFont.Clone().WithOrientation(EnumTextOrientation.Center), nameText, "className");
        composer.AddIconButton("right", _ => dialog.ChangeClass(1), rightArrow);
        y += rowHeight + Layout.SectionPadding + Layout.SectionGap;

        // Class background and traits under the name.
        ScrollArea description = new(frame.RightX, y, frame.RightWidth, frame.ContentBottom - y);
        composer.AddInset(description.Outer, 3);
        composer.BeginClip(description.Clip);
        composer.AddRichtext("", CairoFont.WhiteDetailText(), ElementBounds.Fixed(0, 0, description.InnerWidth, description.Clip.fixedHeight).WithParent(description.Clip), "characterDesc");
        composer.EndClip();
        composer.AddVerticalScrollbar(dialog.OnNewScrollbarValue, description.Scrollbar, "scrollbar");

        composer.AddSmallButton(Lang.Get("Confirm Class"), dialog.OnNextImpl, frame.ConfirmButtonBounds());

        // ChangeClass reads ClipHeight when it sizes the scrollbar.
        dialog.ClipHeight = (float)description.Clip.fixedHeight;
        Layout.AfterCompose(() => dialog.ChangeClass(0));

        dialog.OnToggleDressOnOff(on: false);
    }
}
