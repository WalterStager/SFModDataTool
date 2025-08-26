using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using CUE4Parse.Compression;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.UE4.Assets;
using Newtonsoft.Json;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.UObject;

namespace SFModDataExtractor;

class TestCUE4Parse {
    // based on satisfactory-dev/asset-http
    private string SATISFACTORY_PATH = "D:\\EvenMoreSteamExtras\\steamapps\\common\\Satisfactory\\FactoryGame\\";
    private DefaultFileProvider? __provider = null;
    private DefaultFileProvider provider {
        get {
            if (__provider == null) {
                // OodleHelper.DownloadOodleDll();
                OodleHelper.Initialize(Path.GetFullPath(Path.Combine("./", OodleHelper.OODLE_DLL_NAME)));
                __provider = new DefaultFileProvider(
                    directory: SATISFACTORY_PATH,
                    searchOption: SearchOption.AllDirectories,
                    versions: new VersionContainer(EGame.GAME_UE5_3, ETexturePlatform.DesktopMobile),
                    StringComparer.InvariantCulture
                );
                var mc = new FileUsmapTypeMappingsProvider(
                    "C:\\Users\\plent\\source\\SFModDataTool\\SFModDataExtractor\\FactoryGame.usmap"
                );
                __provider.MappingsContainer = mc;
                __provider.Initialize();
                __provider.SubmitKey(new FGuid(), new FAesKey(($"0x{new string('0', 64)}")));
                // __provider.LoadLocalization(ELanguage.English);
            }

            return __provider;
        }
    }

    public static void jsonPrettyPrint(Object? o) {
        Console.WriteLine(JsonConvert.SerializeObject(o, Formatting.Indented));
    }

    public static void main() {
        // "FactoryGame/Mods/BlankOmniWorld/Content/Node/Icon-Omni.uasset
        // "Icon-Omni.uasset"

        TestCUE4Parse te = new TestCUE4Parse();
        IPackage p1 = te.provider.LoadPackage("FactoryGame/Mods/BlankOmniWorld/Content/Node/Desc_Omni.uasset");
        // jsonPrettyPrint(p1);
        // for (int i = 0; i < p1.ExportMapLength; i++) {
        //     jsonPrettyPrint(p1.GetExport(i));
        // }

        try {
            UObject o1 = te.provider.LoadPackage("FactoryGame/Mods/BlankOmniWorld/Content/Node/Icon-Omni").GetExport(0);
            jsonPrettyPrint(o1);
        }
        catch (Exception e) {
            Console.WriteLine($"Failed {e.Message}");
        }

        try {
            UObject o1 = te.provider.LoadPackage("FactoryGame/Mods/BlankOmniWorld/Content/Node/Icon-Omni").GetExport(0);
            jsonPrettyPrint(o1);
        }
        catch (Exception e) {
            Console.WriteLine($"Failed {e.Message}");
        }

        // try {
        //     UObject o1 = te.provider.LoadPackageObject(te.provider.FixPath("FactoryGame/Mods/BlankOmniWorld/Content/Node/Icon-Omni.uasset"), "Texture2D'Icon-Omni'");
        //     jsonPrettyPrint(o1);
        // }
        // catch (Exception e) {
        //     Console.WriteLine($"Failed {e.Message}");
        // }

        // try {
        //     UTexture2D o1 = te.provider.LoadPackageObject<UTexture2D>(te.provider.FixPath("FactoryGame/Mods/BlankOmniWorld/Content/Node/Icon-Omni.uasset"), "Icon-Omni");
        //     jsonPrettyPrint(o1);
        // }
        // catch (Exception e) {
        //     Console.WriteLine($"Failed {e.Message}");
        // }

        // FactoryGame/Mods/BlankOmniWorld/Content/Node/Desc_Omni.uasset
        // FactoryGame/Mods/BlankOmniWorld/Content/Node/Icon-Omni.uasset
    }
}