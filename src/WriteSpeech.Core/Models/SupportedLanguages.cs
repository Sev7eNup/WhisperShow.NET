namespace WriteSpeech.Core.Models;

public static class SupportedLanguages
{
    public static readonly IReadOnlyList<(string Code, string Name, string Flag)> All =
    [
        ("de", "German", "/Resources/Flags/de.png"),
        ("en", "English", "/Resources/Flags/en.png"),
        ("fr", "French", "/Resources/Flags/fr.png"),
        ("es", "Spanish", "/Resources/Flags/es.png"),
        ("it", "Italian", "/Resources/Flags/it.png"),
        ("pt", "Portuguese", "/Resources/Flags/pt.png"),
        ("nl", "Dutch", "/Resources/Flags/nl.png"),
        ("pl", "Polish", "/Resources/Flags/pl.png"),
        ("ru", "Russian", "/Resources/Flags/ru.png"),
        ("uk", "Ukrainian", "/Resources/Flags/uk.png"),
        ("zh", "Chinese", "/Resources/Flags/zh.png"),
        ("ja", "Japanese", "/Resources/Flags/ja.png"),
        ("ko", "Korean", "/Resources/Flags/ko.png"),
        ("ar", "Arabic", "/Resources/Flags/ar.png"),
        ("tr", "Turkish", "/Resources/Flags/tr.png"),
        ("sv", "Swedish", "/Resources/Flags/sv.png"),
        ("da", "Danish", "/Resources/Flags/da.png"),
        ("no", "Norwegian", "/Resources/Flags/no.png"),
        ("fi", "Finnish", "/Resources/Flags/fi.png"),
        ("cs", "Czech", "/Resources/Flags/cs.png"),
    ];
}
