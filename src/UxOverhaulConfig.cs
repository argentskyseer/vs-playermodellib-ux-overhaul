using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace PlayerModelLibUxOverhaul;

public enum PreviewBackgroundMode
{
    /// <summary>See-through with the world behind it blurred.</summary>
    Blur,
    /// <summary>See-through, no blur.</summary>
    Clear,
    /// <summary>PlayerModelLib's dark inset panel.</summary>
    Panel
}

/// <summary>
/// Stored in ModConfig/playermodellib-ux-overhaul.json. Sizes are in unscaled GUI units.
/// </summary>
public sealed class UxOverhaulConfig
{
    public const string FileName = "playermodellib-ux-overhaul.json";

    public const int CurrentVersion = 4;

    /// <summary>Lets newer versions of the mod update defaults the user never changed. Don't edit.</summary>
    public int ConfigVersion { get; set; }

    /// <summary>Fraction of the screen width the character dialog may use.</summary>
    public float ScreenWidthFraction { get; set; } = 0.8f;

    /// <summary>Fraction of the screen height the character dialog may use.</summary>
    public float ScreenHeightFraction { get; set; } = 0.8f;

    /// <summary>Optional size limits in GUI units; 0 means no limit.</summary>
    public int MaxWidth { get; set; }

    public int MaxHeight { get; set; }

    /// <summary>
    /// Widest a column of text or controls may get, in characters of the GUI font. Around 65 keeps lines
    /// comfortable to read; any extra width goes to the character preview instead.
    /// </summary>
    public int ColumnWidthChars { get; set; } = 65;

    /// <summary>Share of the dialog width given to the character preview (0.2-0.7).</summary>
    public float PreviewWidthFraction { get; set; } = 0.42f;

    /// <summary>How much of the preview panel's height a full-size character fills (0-1).</summary>
    public float PreviewFill { get; set; } = 0.8f;

    /// <summary>Default zoom on the skin tab, relative to the full-body view on the other tabs.</summary>
    public float SkinTabZoom { get; set; } = 1.25f;

    /// <summary>Background of the character preview: Blur, Clear or Panel.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public PreviewBackgroundMode PreviewBackground { get; set; } = PreviewBackgroundMode.Blur;

    /// <summary>0 = barely blurred, 1 = just soft colour.</summary>
    public float BlurStrength { get; set; } = 0.5f;

    /// <summary>How much the see-through preview is darkened so the character stands out (0-1).</summary>
    public float PreviewDarken { get; set; } = 0.25f;

    /// <summary>
    /// Icons for the main tabs, by tab code ("model", "skin", "class" or another mod's tab), as asset paths
    /// such as "game:textures/icons/foo.svg". An empty value hides a bundled icon.
    /// </summary>
    public Dictionary<string, string> CharacterTabIcons { get; set; } = new();

    /// <summary>Icons for the skin part tabs, by tab code ("body", "head", "presets"...). Same format.</summary>
    public Dictionary<string, string> SkinPartTabIcons { get; set; } = new();

    /// <summary>
    /// Bring an older config up to date. Only values still at an old default are replaced, so anything the
    /// user set by hand is kept.
    /// </summary>
    public void Upgrade()
    {
        if (ConfigVersion < 2)
        {
            // 0.1.x defaults: a smaller, capped window.
            if (ScreenWidthFraction == 0.8f) ScreenWidthFraction = 0.95f;
            if (ScreenHeightFraction == 0.85f) ScreenHeightFraction = 0.92f;
            if (MaxWidth == 1500) MaxWidth = 0;
            if (MaxHeight == 950) MaxHeight = 0;
        }
        if (ConfigVersion < 3)
        {
            // 0.2.x defaults: nearly full screen.
            if (ScreenWidthFraction == 0.95f) ScreenWidthFraction = 1f;
            if (ScreenHeightFraction == 0.92f) ScreenHeightFraction = 1f;
        }
        if (ConfigVersion < 4)
        {
            // 0.3.x defaults: full screen.
            if (ScreenWidthFraction == 1f) ScreenWidthFraction = 0.8f;
            if (ScreenHeightFraction == 1f) ScreenHeightFraction = 0.8f;
        }
        ConfigVersion = CurrentVersion;
    }

    public void Sanitize()
    {
        ScreenWidthFraction = Math.Clamp(ScreenWidthFraction, 0.3f, 1f);
        ScreenHeightFraction = Math.Clamp(ScreenHeightFraction, 0.3f, 1f);
        MaxWidth = MaxWidth <= 0 ? 0 : Math.Max(MaxWidth, Layout.MinContentWidth);
        MaxHeight = MaxHeight <= 0 ? 0 : Math.Max(MaxHeight, Layout.MinContentHeight);
        ColumnWidthChars = Math.Clamp(ColumnWidthChars, 30, 200);
        PreviewWidthFraction = Math.Clamp(PreviewWidthFraction, 0.2f, 0.7f);
        PreviewFill = Math.Clamp(PreviewFill, 0.3f, 1f);
        SkinTabZoom = Math.Clamp(SkinTabZoom, 0.5f, 3f);
        BlurStrength = Math.Clamp(BlurStrength, 0f, 1f);
        PreviewDarken = Math.Clamp(PreviewDarken, 0f, 1f);
        CharacterTabIcons = new Dictionary<string, string>(CharacterTabIcons ?? new(), StringComparer.OrdinalIgnoreCase);
        SkinPartTabIcons = new Dictionary<string, string>(SkinPartTabIcons ?? new(), StringComparer.OrdinalIgnoreCase);
    }
}
