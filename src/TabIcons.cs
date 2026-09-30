using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace PlayerModelLibUxOverhaul;

/// <summary>
/// Finds the icon for a tab. The config can name one per tab code (an empty string hides the icon);
/// otherwise any mod or model pack can ship <c>textures/icons/&lt;category&gt;/&lt;code&gt;.svg</c> or
/// <c>.png</c>. A code with a domain ("othermod:tab") only looks in that mod's assets. Codes are matched
/// ignoring case, since models write them as "Head" but asset paths are always lower case.
/// </summary>
public static class TabIcons
{
    /// <summary>Main dialog tabs: model, skin, class and tabs added by other mods.</summary>
    public const string CharacterTabs = "charactertab";

    /// <summary>Skin part tabs on the skin tab: body, head, presets...</summary>
    public const string SkinPartTabs = "skinparttab";

    private static readonly string[] Extensions = { ".svg", ".png" };

    // Assets don't change while the game runs, so each lookup is done once.
    private static readonly Dictionary<(string category, string code), AssetLocation?> Cache = new();

    public static AssetLocation? Find(ICoreClientAPI api, string category, string code)
    {
        if (!Cache.TryGetValue((category, code), out AssetLocation? icon))
        {
            icon = TryConfig(api, category, code, out AssetLocation? configured) ? configured : FromAssets(api, category, code);
            Cache[(category, code)] = icon;
        }
        return icon;
    }

    /// <returns>True when the config has an entry for this tab, even one that hides the icon.</returns>
    private static bool TryConfig(ICoreClientAPI api, string category, string code, out AssetLocation? icon)
    {
        icon = null;
        UxOverhaulConfig config = UxOverhaulModSystem.Config;
        Dictionary<string, string> icons = category == SkinPartTabs ? config.SkinPartTabIcons : config.CharacterTabIcons;
        if (!icons.TryGetValue(code, out string? path)) return false;
        if (string.IsNullOrWhiteSpace(path)) return true;

        AssetLocation location = new(path);
        if (api.Assets.Exists(location))
        {
            icon = location;
            return true;
        }
        api.Logger.Warning("[playermodellib-ux-overhaul] Icon '{0}' for tab '{1}' was not found, looking for a bundled one instead.", path, code);
        return false;
    }

    private static AssetLocation? FromAssets(ICoreClientAPI api, string category, string code)
    {
        int colon = code.IndexOf(':');
        string? domain = colon > 0 ? code[..colon] : null;
        string name = (colon > 0 ? code[(colon + 1)..] : code).ToLowerInvariant();
        string folder = $"textures/icons/{category}/";

        foreach (string extension in Extensions)
        {
            string path = folder + name + extension;
            if (domain != null)
            {
                AssetLocation location = new(domain, path);
                if (api.Assets.Exists(location)) return location;
                continue;
            }
            IAsset? asset = api.Assets.GetMany(folder, null, false).FirstOrDefault(asset => asset.Location.Path == path);
            if (asset != null) return asset.Location;
        }
        return null;
    }
}
