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

    public Dictionary<string, UassetRecipe> Recipes = new Dictionary<string, UassetRecipe>();
    public Dictionary<string, UassetPart> Parts = new Dictionary<string, UassetPart>();
    public Dictionary<string, UAssetMachine> Machines = new Dictionary<string, UAssetMachine>();
    public Dictionary<string, HashSet<UassetFile>> ModFiles = new Dictionary<string, HashSet<UassetFile>>();
    public HashSet<UassetFile> AllUassetFiles = new HashSet<UassetFile>();
    public HashSet<string> CsvFiles = new HashSet<string>();
    public Dictionary<string, UassetFile> assetPathToFileCache = new Dictionary<string, UassetFile>();

    public string GetModName(string filename) {
        if (filename.Contains("Mods")) {
            Match modNameMatch = Regex.Match(filename, @"/Mods/(\w+)/");
            string? modName = modNameMatch.Groups[1].Value;
            if (modName == null || modName == "") {
                throw new Exception($"Could not get mod from filename {filename}");
            }
            return modNameMatch.Groups[1].Value;
        }
        else {
            return "FactoryGame";
        }
    }

    public UassetFile NormalizeAndMatchPath(string assetPath) {
        UassetFile? result;
        if (assetPathToFileCache.TryGetValue(Path.ChangeExtension(assetPath, null), out result)) {
            return result;
        }

        string path = Path.ChangeExtension(assetPath, ".uasset").Replace('\\', '/');
        IEnumerable<string> parts = assetPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Reverse();
        string mod = GetModName(path);

        int? maxCount = null;
        foreach (UassetFile c in ModFiles[mod]) {
            int newCount = SFModUtility.CountCommonPrefix(parts, c.File.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Reverse());
            if (result == null || newCount > maxCount) {
                result = c;
                maxCount = newCount;
            }
        }

        if (result == null) {
            throw new Exception($"Could not match asset path {assetPath}");
        }

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

            UassetFile uassetFile = new UassetFile(prov, filename);

            if (!prov.ModFiles.ContainsKey(uassetFile.Mod)) {
                prov.ModFiles.Add(uassetFile.Mod, new HashSet<UassetFile>());
            }

            prov.AllUassetFiles.Add(uassetFile);
            prov.ModFiles[uassetFile.Mod].Add(uassetFile);
        }

        Console.WriteLine($"Uasset files {prov.AllUassetFiles.Count}");
        Console.WriteLine($"CSV files {prov.CsvFiles.Count}");
        Console.WriteLine($"Mod file counts");
        foreach ((string modName, HashSet<UassetFile> modFiles) in prov.ModFiles) {
            Console.WriteLine($"\t{modName}={modFiles.Count}");
        }
    }

    public void doTheThing() {
        
    }
}