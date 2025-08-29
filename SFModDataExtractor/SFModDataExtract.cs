using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using CUE4Parse.Compression;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.Encryption.Aes;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using CUE4Parse.UE4.Objects.UObject;
using Newtonsoft.Json;
using SkiaSharp;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse_Conversion.Textures;
using SFModDataMerger;

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

    public Dictionary<string, UassetRecipe> FileToRecipe = new Dictionary<string, UassetRecipe>();
    public Dictionary<string, UassetFile> FileToBuildingRecipe = new Dictionary<string, UassetFile>();
    public Dictionary<string, UassetPart> FileToPart = new Dictionary<string, UassetPart>();
    public Dictionary<string, UAssetMachine> FileToMachine = new Dictionary<string, UAssetMachine>();
    public Dictionary<string, HashSet<UassetFile>> FilesByMod = new Dictionary<string, HashSet<UassetFile>>();
    public HashSet<UassetFile> AllUassetFiles = new HashSet<UassetFile>();
    public HashSet<string> CsvFiles = new HashSet<string>();
    public Dictionary<string, UassetFile> AssetPathToFile = new Dictionary<string, UassetFile>();
    public HashSet<UassetFile> MachinesToSetupLater = new HashSet<UassetFile>();

    public UassetFile NormalizeAndMatchPath(string assetPath) {
        UassetFile? result;
        if (AssetPathToFile.TryGetValue(Path.ChangeExtension(assetPath, null), out result)) {
            return result;
        }

        string path = Path.ChangeExtension(assetPath, ".uasset").Replace('\\', '/');
        IEnumerable<string> parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Where(p => p != "").Reverse();
        if (parts.Count() <= 0) {
            throw new Exception($"Could not get mod name from asset path {assetPath}");
        }
        string mod = parts.Last();
        mod = mod == "Game" ? "FactoryGame" : mod;
        HashSet<UassetFile> modFiles;
        try {
            modFiles = FilesByMod[mod];
        }
        catch (Exception e) {
            throw new Exception($"Got invalid mod name from path {assetPath}, {e.Message}");
        }

        int? maxCount = null;
        foreach (UassetFile modFile in modFiles) {
            IEnumerable<string> modFileParts = modFile.File.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Where(p => p != "").Reverse();
            int newCount = SFModUtility.CountCommonPrefix(parts, modFileParts);

            if (result == null || newCount > maxCount) {
                result = modFile;
                maxCount = newCount;
            }
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
        if (uf.type != UassetType.RecipeDesc) {
            throw new Exception($"Not a recipe file {uf.File}");
        }

        string? displayName = uf.GetString(0, "Name");
        if (displayName == null) {
            throw new Exception($"Could not get name for {uf.File}");
        }
        UassetRecipe recipe = new UassetRecipe { UFile = uf, DisplayName = displayName };

        int defObjInd = uf.GetDefaultObjectIndex();

        recipe.ManufacturingDuration = uf.GetDouble(defObjInd, "Properties.mManufactoringDuration");
        recipe.VariablePowerConstant = uf.GetInt(defObjInd, "Properties.mVariablePowerConsumptionConstant");
        recipe.VariablePowerFactor = uf.GetInt(defObjInd, "Properties.mVariablePowerConsumptionFactor");

        JToken? producedInToken = uf.GetToken(defObjInd, "Properties.mProducedIn");
        bool hasActualMachine = false;
        bool isMachineRecipe = false;
        if (producedInToken != null) {
            foreach (JToken mchToken in producedInToken.Children()) {
                string? assetPathName = mchToken.SelectToken("AssetPathName")?.ToString();
                if (assetPathName != null && assetPathName != "") {
                    if (assetPathName == "/Game/FactoryGame/Equipment/BuildGun/BP_BuildGun.BP_BuildGun_C" ||
                        assetPathName == "/Script/FactoryGame.FGBuildGun") {
                        isMachineRecipe = true;
                        break;
                    }
                    else if (assetPathName != "/Game/FactoryGame/Buildable/-Shared/WorkBench/BP_WorkshopComponent.BP_WorkshopComponent_C" &&
                        assetPathName != "/Game/FactoryGame/Buildable/-Shared/WorkBench/BP_WorkBenchComponent.BP_WorkBenchComponent_C" &&
                        assetPathName != "/Script/FactoryGame.FGBuildableAutomatedWorkBench" &&
                        assetPathName != "/Game/FactoryGame/Buildable/Factory/AutomatedWorkBench/Build_AutomatedWorkBench.Build_AutomatedWorkBench_C") {
                        hasActualMachine = true;
                        UassetFile mchF = prov.NormalizeAndMatchPath(assetPathName);
                        prov.MachinesToSetupLater.Add(mchF);
                    }
                    else {
                        continue;
                    }
                }
            }
        }

        if (isMachineRecipe) {
            SetupMachineRecipe(uf);
            return;
        }

        if (!hasActualMachine) {
            return;
        }

        JToken? productsToken = uf.GetToken(defObjInd, "Properties.mProduct");
        if (productsToken != null) {
            foreach (JToken prodToken in productsToken.Children()) {
                string? objPath = prodToken.SelectToken("ItemClass.ObjectPath")?.ToString();
                string? amount = prodToken.SelectToken("Amount")?.ToString();
                if (objPath != null && amount != null) {
                    UassetFile prodF = prov.NormalizeAndMatchPath(objPath);
                    SetupPart(prodF);

                    recipe.Products.Add((int.Parse(amount), prov.FileToPart[prodF.File]));
                }
            }
        }

        prov.FileToRecipe.Add(uf.File, recipe);
    }

    private void SetupMachine(UassetFile uf) {
        if (prov.FileToMachine.ContainsKey(uf.File)) {
            return;
        }

        if (uf.type != UassetType.MachineBuild) {
            throw new Exception($"Not a machine build file {uf.File}");
        }

        string? displayName = uf.GetString(0, "Name");
        if (displayName == null) {
            throw new Exception($"Could not get name for {uf.File}");
        }

        UAssetMachine machine = new UAssetMachine { UFile = uf, DisplayName = displayName };
        int defObjInd = uf.GetDefaultObjectIndex();
        machine.PowerConsumption = uf.GetInt(defObjInd, "Properties.mPowerConsumption");
        machine.PowerConsumptionExponent = uf.GetDouble(defObjInd, "Properties.mPowerConsumptionExponent");
        machine.ProductionShardSlotSize = uf.GetInt(defObjInd, "Properties.mProductionShardSlotSize");
        machine.ProductionShardBoostMultiplier = uf.GetDouble(defObjInd, "Properties.mProductionShardBoostMultiplier");
        machine.BasePowerProduction = uf.GetInt(defObjInd, "Properties.mBasePowerProduction");
        machine.BaseBoostPercentage = uf.GetDouble(defObjInd, "Properties.mBaseBoostPercentage");
        
        string? fuelClass = uf.GetString(defObjInd, "Properties.AssetPathname");
        if (fuelClass != null) {
            UassetFile fuelClassFile = prov.NormalizeAndMatchPath(fuelClass);
            int fuelClassDefObjInd = fuelClassFile.GetDefaultObjectIndex();
            machine.BoostPercentage  = fuelClassFile.GetDouble(fuelClassDefObjInd, "Properties.mBoostPercentage");
        }

        if (prov.FileToBuildingRecipe.ContainsKey(uf.File)) {
            UassetFile machineRecipeFile = prov.FileToBuildingRecipe[uf.File];
            int mrDefObjInd = machineRecipeFile.GetDefaultObjectIndex();

            JToken? ingredientsToken = machineRecipeFile.GetToken(mrDefObjInd, "Properties.mIngredients");
            if (ingredientsToken != null) {
                foreach (JToken ingToken in ingredientsToken.Children()) {
                    string? objPath = ingToken.SelectToken("ItemClass.ObjectPath")?.ToString();
                    string? amount = ingToken.SelectToken("Amount")?.ToString();
                    if (objPath != null && amount != null) {
                        UassetFile ingF = prov.NormalizeAndMatchPath(objPath);
                        SetupPart(ingF);

                        machine.Ingredients.Add((int.Parse(amount), prov.FileToPart[ingF.File]));
                    }
                }
            }

            JToken? productsToken = machineRecipeFile.GetToken(mrDefObjInd, "Properties.mProduct");
            if (productsToken != null) {
                // there should only be one product in a machine recipe
                foreach (JToken prodToken in productsToken.Children()) {
                    string? objPath = prodToken.SelectToken("ItemClass.ObjectPath")?.ToString();
                    if (objPath != null) {
                        UassetFile machineDescFile = prov.NormalizeAndMatchPath(objPath);
                        int bdfDefObjInd = machineDescFile.GetDefaultObjectIndex();
                        string? iconObjPath = machineDescFile.GetString(bdfDefObjInd, "Properties.mSmallIcon.ObjectPath");
                        if (iconObjPath == null) {
                            iconObjPath = machineDescFile.GetString(bdfDefObjInd, "Properties.mPersistentBigIcon.ObjectPath");
                        }
                        if (iconObjPath == null) {
                            throw new Exception($"Couldn't get icon path for {machineDescFile.File}");
                        }
                        UassetFile iconUFile = prov.NormalizeAndMatchPath(iconObjPath);
                        machine.Icon = GetTexture(iconUFile, SFModUtility.GetAssetPathIndex(iconObjPath));
                    }
                }
            }
        }

        prov.FileToMachine.Add(uf.File, machine);
    }


    public void SetupPart(UassetFile uf) {
        if (prov.FileToPart.ContainsKey(uf.File)) {
            return;
        }

        if (uf.type != UassetType.ItemDesc) {
            throw new Exception($"Not a part file {uf.File}");
        }

        string? displayName = uf.GetString(0, "Name");
        if (displayName == null) {
            throw new Exception($"Could not get name for {uf.File}");
        }
        UassetPart part = new UassetPart { UFile = uf, DisplayName = displayName };

        int defObjInd = uf.GetDefaultObjectIndex();

        string? iconObjPath = uf.GetString(defObjInd, "Properties.mSmallIcon.ObjectPath");
        if (iconObjPath == null) {
            iconObjPath = uf.GetString(defObjInd, "Properties.mPersistentBigIcon.ObjectPath");
        }
        if (iconObjPath == null) {
            throw new Exception($"Couldn't get icon path for {uf.File}");
        }
        UassetFile iconUFile = prov.NormalizeAndMatchPath(iconObjPath);
        part.Icon = GetTexture(iconUFile, SFModUtility.GetAssetPathIndex(iconObjPath));

        prov.FileToPart.Add(uf.File, part);
    }

    public static SKBitmap[]? GetTexture(UassetFile textureFile, int objIndex) {
        // if (textureFile.Mod == "FactoryGame") {
        //     return null;
        // }

        if (textureFile.type != UassetType.Texture) {
            throw new Exception($"Not a texture file {textureFile.File}");
        }

        UObject uObj = textureFile.GetUObject(objIndex);
        if (uObj is not UTexture) {
            throw new Exception($"Not a texture object {textureFile.File}");
        }
        UTexture tex = (UTexture)uObj;
        SKBitmap?[]? bitmaps = new[] { tex.Decode(ETexturePlatform.DesktopMobile) };
        switch (tex) {
            case UTexture2DArray textureArray:
                bitmaps = textureArray.DecodeTextureArray(ETexturePlatform.DesktopMobile);
                break;
            case UTextureCube:
                bitmaps[0] = bitmaps[0]?.ToPanorama();
                break;
        }
        if (bitmaps == null) {
            throw new Exception($"Could not extract bitmaps {textureFile.File}");
        }

        return bitmaps.Where(b => b != null).Cast<SKBitmap>().ToArray();

        // foreach (SKBitmap? bitmap in bitmaps) {
        //     if (bitmap is null) continue;
        //     SKData bytes = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        //     if (!Path.Exists(savePath)) {
        //         File.WriteAllBytes(savePath, bytes.ToArray());
        //     }
        // }
        // return true;

    }

    // recipe file -> desc file -> build file (save the build file so that it can be used to look up recipe file later)
    public void SetupMachineRecipe(UassetFile machineRecipeFile) {
        int defObjInd = machineRecipeFile.GetDefaultObjectIndex();

        JToken? productsToken = machineRecipeFile.GetToken(defObjInd, "Properties.mProduct");
        if (productsToken != null) {
            // there should only be one product in a machine recipe
            foreach (JToken prodToken in productsToken.Children()) {
                string? objPath = prodToken.SelectToken("ItemClass.ObjectPath")?.ToString();
                if (objPath != null) {
                    UassetFile machineDescFile = prov.NormalizeAndMatchPath(objPath);
                    int bdfDefObjInd = machineDescFile.GetDefaultObjectIndex();
                    string? buildFilePath = machineDescFile.GetString(bdfDefObjInd, "Properties.mBuildableClass.ObjectPath");
                    if (buildFilePath != null) {
                        UassetFile machineBuildFile = prov.NormalizeAndMatchPath(buildFilePath);
                        prov.FileToBuildingRecipe.Add(machineBuildFile.File, machineRecipeFile);
                        return;
                    }
                }
            }
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

        // machine setup is delayed so that machine recipes are already found
        foreach (UassetFile machine in prov.MachinesToSetupLater) {
            SetupMachine(machine);
        }

        Console.WriteLine($"Recipes {prov.FileToRecipe.Count}");
        Console.WriteLine($"Parts {prov.FileToPart.Count}");
        Console.WriteLine($"Machines {prov.FileToMachine.Count}");
        Console.WriteLine($"MachineRecipes {prov.FileToBuildingRecipe.Count}");

        foreach ((string modName, HashSet<UassetFile> modFiles) in prov.FilesByMod) {
            GameData modGameData = new GameData {
                Machines = new List<GameDataMachine>(),
                MultiMachines = new List<GameDataMultiMachine>(),
                Parts = new List<GameDataItem>(),
                Recipes = new List<GameDataRecipe>()
            };

            foreach (UassetFile uf in modFiles) {
                if (prov.FileToRecipe.ContainsKey(uf.File)) {
                    modGameData.Recipes.ToList().Concat(prov.FileToRecipe[uf.File].ToGameDataRecipe());
                }
                else if (prov.FileToMachine.ContainsKey(uf.File)) {
                    modGameData.Machines.ToList().Add(prov.FileToMachine[uf.File].ToGameDataMachine());
                }
                else if (prov.FileToPart.ContainsKey(uf.File)) {
                    modGameData.Parts.ToList().Add(prov.FileToPart[uf.File].ToGameDataItem());
                }
            }

            Directory.CreateDirectory(modName);
            modGameData.WriteGameData(Path.Combine(modName, $"game_data_{modName}.json"));
        }
    }
}