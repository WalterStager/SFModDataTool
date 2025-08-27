using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using CUE4Parse.Compression;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.Encryption.Aes;
using System.Text.RegularExpressions;

namespace SFModDataExtractor;

class SFModDataProvider {
    // based on satisfactory-dev/asset-http
    private string SATISFACTORY_PATH = "D:\\EvenMoreSteamExtras\\steamapps\\common\\Satisfactory\\";
    private EGame Version = EGame.GAME_UE5_3;

    private DefaultFileProvider? __provider = null;
    public DefaultFileProvider FileProvider {
        get {
            if (__provider == null) {
                // OodleHelper.DownloadOodleDll();
                OodleHelper.Initialize(Path.GetFullPath(Path.Combine("./", OodleHelper.OODLE_DLL_NAME)));
                __provider = new DefaultFileProvider(
                    directory: Path.Combine(SATISFACTORY_PATH, "FactoryGame\\"),
                    searchOption: SearchOption.AllDirectories,
                    versions: new VersionContainer(Version, ETexturePlatform.DesktopMobile),
                    StringComparer.InvariantCulture
                );
                var mc = new FileUsmapTypeMappingsProvider(
                    Path.Combine(SATISFACTORY_PATH, "CommunityResources\\FactoryGame.usmap")
                );
                __provider.MappingsContainer = mc;
                __provider.Initialize();
                __provider.SubmitKey(new FGuid(), new FAesKey(($"0x{new string('0', 64)}")));
                // __provider.LoadLocalization(ELanguage.English);
            }

            return __provider;
        }
    }

    public Dictionary<string, UassetRecipe> NameToRecipe = new Dictionary<string, UassetRecipe>();
    public Dictionary<string, UassetPart> NameToPart = new Dictionary<string, UassetPart>();
    public Dictionary<string, UAssetMachine> NameToMachine = new Dictionary<string, UAssetMachine>();
    public Dictionary<string, HashSet<UassetFile>> FilesByMod = new Dictionary<string, HashSet<UassetFile>>();
    public HashSet<UassetFile> AllUassetFiles = new HashSet<UassetFile>();
    public HashSet<string> CsvFiles = new HashSet<string>();
    public Dictionary<string, UassetFile> AssetPathToFile = new Dictionary<string, UassetFile>();

    public string GetModName(string filename) {
        if (filename.Contains("Mods")) {
            string? modName = null;
            string[] parts = filename.Split(Path.AltDirectorySeparatorChar);
            for (int i = 0; i < parts.Length; i++) {
                if (parts[i] == "Mods") {
                    modName = parts[i + 1];
                    break;
                }
            }
            if (modName == null || modName == "") {
                throw new Exception($"Could not get mod from filename {filename}");
            }
            return modName;
        }
        else {
            return "FactoryGame";
        }
    }

    public UassetFile NormalizeAndMatchPath(string assetPath) {
        UassetFile? result;
        if (AssetPathToFile.TryGetValue(Path.ChangeExtension(assetPath, null), out result)) {
            return result;
        }

        string path = Path.ChangeExtension(assetPath, ".uasset").Replace('\\', '/');
        IEnumerable<string> parts = assetPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Reverse();
        string mod = GetModName(path);
        try {
            int? maxCount = null;
            foreach (UassetFile c in FilesByMod[mod]) {
                int newCount = SFModUtility.CountCommonPrefix(parts, c.File.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Reverse());
                if (result == null || newCount > maxCount) {
                    result = c;
                    maxCount = newCount;
                }
            }
        }
        catch (Exception e) {
            throw new Exception($"ERROR {assetPath} : {e.Message}");
        }

        if (result == null) {
            throw new Exception($"Could not match asset path {assetPath}");
        }

        AssetPathToFile.Add(Path.ChangeExtension(assetPath, null), result);

        return result;
    }
}

class SFModDataExtract {
    private SFModDataProvider prov;

    public SFModDataExtract() {
        prov = new SFModDataProvider();

        foreach (string filename in prov.FileProvider.Files.Keys) {
            if (!filename.StartsWith("FactoryGame")) {
                continue;
            }

            if (Path.GetExtension(filename) == ".csv") {
                prov.CsvFiles.Add(filename);
                continue;
            }

            if (Path.GetExtension(filename) != ".uasset") {
                continue;
            }

            UassetFile uf = new UassetFile(prov, filename);

            if (!prov.FilesByMod.ContainsKey(uf.Mod)) {
                prov.FilesByMod.Add(uf.Mod, new HashSet<UassetFile>());
            }

            prov.AllUassetFiles.Add(uf);
            prov.FilesByMod[uf.Mod].Add(uf);
        }

        Console.WriteLine($"Uasset files {prov.AllUassetFiles.Count}");
        Console.WriteLine($"CSV files {prov.CsvFiles.Count}");
        Console.WriteLine($"Mod file counts");
        foreach ((string modName, HashSet<UassetFile> modFiles) in prov.FilesByMod) {
            Console.WriteLine($"\t{modName}={modFiles.Count}");
        }
    }

    public void SetupRecipe(UassetFile uf) {
        string? displayName = uf.GetString(0, "Name");
        if (displayName == null) {
            throw new Exception($"Could not get name for {uf.File}");
        }
        UassetRecipe ur = new UassetRecipe { UFile = uf, DisplayName = displayName };

        string? defObjPath = uf.GetString(0, "ClassDefaultObject.ObjectPath");
        if (defObjPath == null) {
            throw new Exception($"Couldn't get default object path for {uf.File}");
        }
        int defObjInd = SFModUtility.GetAssetPathIndex(defObjPath);
        string? duration = uf.GetString(defObjInd, "mManufactoringDuration");
        if (duration != null) {
            ur.ManufacturingDuration = float.Parse(duration);
        }
    }

    public void doTheThing() {
        foreach ((string modName, HashSet<UassetFile> modFiles) in prov.FilesByMod) {
            foreach (UassetFile uf in modFiles) {
                if (uf.type == UassetType.RecipeDesc) {
                    SetupRecipe(uf);
                }
            }
        }
    }
}