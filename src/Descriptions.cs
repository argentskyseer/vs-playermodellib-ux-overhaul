using System.Collections.Generic;
using System.Linq;
using System.Text;
using PlayerModelLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace PlayerModelLibUxOverhaul;

/// <summary>
/// The model and class description texts, as PlayerModelLib 1.23.2 writes them but with larger section
/// headings ("Traits", "Available classes"...) and the available classes listed one per line like traits.
/// </summary>
public static class Descriptions
{
    private const int HeadingFontSize = 20;

    /// <summary>A section heading from the lang file, enlarged and in the decorative font. Its own markup (bold) is kept.</summary>
    private static string Heading(string langKey) =>
        $"<font size=\"{HeadingFontSize}\" family=\"{GuiStyle.DecorativeFontName}\">{Lang.Get(langKey).Trim()}</font>";

    /// <summary>A model's own description from the lang file ("modeldesc-..."), or "" if it has none.</summary>
    public static string ModelLore(string model)
    {
        string[] parts = model.Split(':');
        if (parts.Length != 2) parts = new[] { "game", model };
        string key = parts[0] + ":modeldesc-" + parts[1];
        return Lang.HasTranslation(key, true, true) ? Lang.Get(key) : "";
    }

    /// <summary>
    /// The model tab's traits: PlayerModelLib's own description text without the model's description at the
    /// start (see <see cref="ModelLore"/>) and the available classes at the end (see <see cref="ModelClasses"/>),
    /// so its missing-trait warnings still appear.
    /// </summary>
    public static string ModelTraits(GuiDialogCreateCustomCharacter dialog, CustomModelsSystem system, string model)
    {
        string text = dialog.CreateModelDescription(system, model);
        string lore = ModelLore(model);
        if (lore.Length > 0 && text.StartsWith(lore, System.StringComparison.Ordinal)) text = text[lore.Length..];
        string classesHeading = Lang.Get("playermodellib:available-classes");
        int classesAt = text.LastIndexOf(classesHeading, System.StringComparison.Ordinal);
        if (classesAt >= 0) text = text[..classesAt];
        return EnlargeHeadings(text.Trim());
    }

    /// <summary>The classes a model can pick, one per line under their heading.</summary>
    public static string ModelClasses(GuiDialogCreateCustomCharacter dialog, CustomModelsSystem system, string model)
    {
        StringBuilder classes = new();
        classes.AppendLine(Heading("playermodellib:available-classes"));
        foreach (CharacterClass characterClass in dialog.GetAvailableClasses(system, model))
        {
            classes.AppendLine(ClassName(characterClass));
        }
        return classes.ToString();
    }

    /// <summary>The class tab's background and traits, built the same way ChangeClass does.</summary>
    public static string Class(GuiDialogCreateCustomCharacter dialog, CharacterClass characterClass, string modelCode)
    {
        Dictionary<string, Trait> traitsByCode = dialog.CharacterSystem.TraitsByCode;
        string[] modelTraitCodes = dialog.CustomModelsSystem.CustomModels[modelCode].ExtraTraits.ToArray();
        IEnumerable<Trait> classTraits = characterClass.Traits
            .Where(code => !modelTraitCodes.Contains(code))
            .Where(traitsByCode.ContainsKey)
            .Select(code => traitsByCode[code])
            .OrderBy(trait => (int)trait.Type);
        Trait[] modelTraits = modelTraitCodes
            .Where(traitsByCode.ContainsKey)
            .Select(code => traitsByCode[code])
            .OrderBy(trait => (int)trait.Type)
            .ToArray();

        StringBuilder description = new();
        string loreKey = "characterdesc-" + GuiDialogCreateCustomCharacter.ProcessDomainForLang(characterClass.Code);
        if (Lang.HasTranslation(loreKey))
        {
            description.AppendLine();
            description.AppendLine(Lang.Get(loreKey));
        }
        description.AppendLine();
        description.AppendLine(Heading("traits-title"));
        GuiDialogCreateCustomCharacter.AppendTraits(description, classTraits);
        if (characterClass.Traits.Length == 0)
        {
            description.AppendLine(Lang.Get("No positive or negative traits"));
        }
        if (modelTraits.Length > 0)
        {
            description.AppendLine();
            description.AppendLine(Heading("model-traits-title"));
            GuiDialogCreateCustomCharacter.AppendTraits(description, modelTraits);
        }
        description.AppendLine();
        return description.ToString();
    }

    private static string ClassName(CharacterClass characterClass) =>
        Lang.Get("characterclass-" + GuiDialogCreateCustomCharacter.ProcessDomainForLang(characterClass.Code));

    private static string EnlargeHeadings(string text)
    {
        foreach (string key in new[] { "traits-title", "model-traits-title" })
        {
            string heading = Lang.Get(key);
            if (heading.Length > 0) text = text.Replace(heading, Heading(key));
        }
        return text;
    }
}
