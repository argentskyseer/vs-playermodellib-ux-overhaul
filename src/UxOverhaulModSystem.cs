using System;
using HarmonyLib;
using PlayerModelLib;
using PlayerModelLibUxOverhaul.Tabs;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace PlayerModelLibUxOverhaul;

public sealed class UxOverhaulModSystem : ModSystem
{
    public const string HarmonyId = "playermodellib-ux-overhaul";

    public static UxOverhaulConfig Config { get; private set; } = new();

    public static PreviewBackdrop? Backdrop { get; private set; }

    /// <summary>This mod's version, shown small in the dialog's title bar.</summary>
    public static string Version { get; private set; } = "";

    private Harmony? _harmony;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        Version = Mod.Info.Version;
        LoadConfig(api);
        Backdrop = new PreviewBackdrop(api);

        _harmony = new Harmony(HarmonyId);
        _harmony.PatchAll(typeof(UxOverhaulModSystem).Assembly);

        GuiDialogCreateCustomCharacter.OnCreated += OnDialogCreated;
    }

    public override void Dispose()
    {
        GuiDialogCreateCustomCharacter.OnCreated -= OnDialogCreated;
        _harmony?.UnpatchAll(HarmonyId);
        _harmony = null;
        Backdrop?.Dispose();
        Backdrop = null;
    }

    private static void OnDialogCreated(GuiDialogCreateCustomCharacter dialog)
    {
        // Only swap tabs that PlayerModelLib registered itself; tabs added by other mods keep their own composers.
        ReplaceTab(dialog, "model", ModelTab.Compose);
        ReplaceTab(dialog, "skin", SkinTab.Compose);
        ReplaceTab(dialog, "class", ClassTab.Compose);
    }

    private static void ReplaceTab(GuiDialogCreateCustomCharacter dialog, string key, GuiDialogCreateCustomCharacter.ComposeTabDelegate composer)
    {
        if (dialog.Tabs.ContainsKey(key))
        {
            dialog.Tabs[key] = composer;
        }
    }

    private void LoadConfig(ICoreClientAPI api)
    {
        try
        {
            Config = api.LoadModConfig<UxOverhaulConfig>(UxOverhaulConfig.FileName) ?? new UxOverhaulConfig();
        }
        catch (Exception exception)
        {
            Mod.Logger.Error("Failed to read {0}, using defaults: {1}", UxOverhaulConfig.FileName, exception);
            Config = new UxOverhaulConfig();
        }
        Config.Upgrade();
        Config.Sanitize();
        api.StoreModConfig(Config, UxOverhaulConfig.FileName);
    }
}
