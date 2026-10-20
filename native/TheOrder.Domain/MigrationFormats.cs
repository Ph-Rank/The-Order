namespace TheOrder.Domain;

public static class MigrationFormats
{
    public static bool Valid(string manager, string format) => (manager, format) is
        ("potatovn", "potatovn-export-json-v1") or ("vnite", "vnite-pouchdb-v4")
        or ("reinamanager", "reinamanager-sqlite-v32") or ("playnite", "playnite10-litedb4")
        or ("lunabox", "lunabox-duckdb-csv-v1");
    public static string Display(string manager) => manager switch
    { "potatovn" => "PotatoVN", "vnite" => "Vnite", "reinamanager" => "ReinaManager", "playnite" => "Playnite", "lunabox" => "LunaBox", _ => "未知管理器" };
}
