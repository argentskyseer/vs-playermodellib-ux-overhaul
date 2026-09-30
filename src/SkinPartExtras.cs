using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace PlayerModelLibUxOverhaul;

/// <summary>
/// Skin part settings in a model config that only this mod reads. PlayerModelLib drops fields it doesn't
/// know (so models using them still work without this mod), which means they're read from the model config
/// assets directly, the same way PlayerModelLib finds them:
/// <list type="bullet">
/// <item><c>"allowColBreak": true</c> — honour the part's "colBreak". PlayerModelLib's column breaks were
/// chosen for its narrow window, and it adds one to every model that doesn't set it, so they're ignored
/// without this.</item>
/// <item><c>"guiDescription": "text or lang key"</c> — shown to the right of the part on the skin tab.</item>
/// </list>
/// </summary>
public static class SkinPartExtras
{
    private const string AllowColBreakKey = "allowColBreak";
    private const string DescriptionKey = "guiDescription";

    private sealed class Extras
    {
        // Keyed by "domain:model|part". Built once, on first use.
        public readonly HashSet<string> AllowColBreak = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Descriptions = new(StringComparer.OrdinalIgnoreCase);
    }

    private static Extras? _extras;

    public static bool AllowColBreak(ICoreClientAPI api, string modelCode, string partCode) =>
        Get(api).AllowColBreak.Contains(Key(modelCode, partCode));

    /// <summary>The part's description, translated if it's a lang key, or null if it has none.</summary>
    public static string? Description(ICoreClientAPI api, string modelCode, string partCode)
    {
        if (!Get(api).Descriptions.TryGetValue(Key(modelCode, partCode), out string? description)) return null;
        return Lang.HasTranslation(description) ? Lang.Get(description) : description;
    }

    private static Extras Get(ICoreClientAPI api) => _extras ??= Load(api);

    private static string Key(string modelCode, string partCode) => modelCode + "|" + partCode;

    private static Extras Load(ICoreClientAPI api)
    {
        Extras extras = new();
        foreach (IAsset asset in api.Assets.GetManyInCategory("config", "customplayermodels/", null, true))
        {
            try
            {
                if (JsonObject.FromJson(asset.ToText()).Token is not JObject models) continue;
                foreach ((string model, JToken? config) in models)
                {
                    if (config is not JObject modelConfig) continue;
                    if (modelConfig.GetValue("skinnableParts", StringComparison.OrdinalIgnoreCase) is not JArray parts) continue;
                    foreach (JToken part in parts)
                    {
                        if (part is not JObject partConfig) continue;
                        string? code = partConfig.GetValue("code", StringComparison.OrdinalIgnoreCase)?.Value<string>();
                        if (code == null) continue;
                        // PlayerModelLib keys models as "<asset domain>:<name in file>".
                        string key = Key(asset.Location.Domain + ":" + model, code);

                        if (partConfig.GetValue(AllowColBreakKey, StringComparison.OrdinalIgnoreCase) is { Type: JTokenType.Boolean } allow && allow.Value<bool>())
                        {
                            extras.AllowColBreak.Add(key);
                        }
                        if (partConfig.GetValue(DescriptionKey, StringComparison.OrdinalIgnoreCase) is { Type: JTokenType.String } description
                            && description.Value<string>() is { Length: > 0 } text)
                        {
                            extras.Descriptions[key] = text;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                api.Logger.Warning("[playermodellib-ux-overhaul] Could not read skin part settings from {0}: {1}", asset.Location, exception.Message);
            }
        }
        api.Logger.Notification("[playermodellib-ux-overhaul] Skin part settings: {0} allowColBreak, {1} guiDescription ({2})",
            extras.AllowColBreak.Count, extras.Descriptions.Count, string.Join(", ", extras.Descriptions.Keys));
        return extras;
    }
}
