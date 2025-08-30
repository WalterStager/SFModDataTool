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
using CUE4Parse.UE4.Readers;
using CsvHelper;
using System.Globalization;

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
    public Dictionary<string, Dictionary<string, DataEntry>> dataTables = new Dictionary<string, Dictionary<string, DataEntry>>();
    // public HashSet<UAssetMachine> MachinesToSetupLater = new HashSet<UAssetMachine>();

    public UassetFile NormalizeAndMatchPath(string assetPath) {
        UassetFile? result;
        if (AssetPathToFile.TryGetValue(Path.ChangeExtension(assetPath, null), out result)) {
            return result;
        }

        string path = Path.ChangeExtension(assetPath, ".uasset").Replace('\\', '/');
        IEnumerable<string> parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Where(p => p != "").Reverse();
        if (parts.Count() <= 0) {
            throw new SFModDataRuntimeException($"Could not get mod name from asset path {assetPath}");
        }
        string mod = parts.Last();
        mod = mod == "Game" ? "FactoryGame" : mod;
        HashSet<UassetFile> modFiles;
        try {
            modFiles = FilesByMod[mod];
        }
        catch (Exception e) {
            throw new SFModDataRuntimeException($"Got invalid mod name from path {assetPath}, {e.Message}");
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
            throw new SFModDataRuntimeException($"Could not match asset path {assetPath}");
        }

        AssetPathToFile.Add(Path.ChangeExtension(assetPath, null), result);

        return result;
    }

    public void ReadCsv(string path, string tableName) {
        dataTables.Add(tableName, new Dictionary<string, DataEntry>());
        FArchive? archive = FileProvider.CreateReader(path);
        StreamReader reader = new StreamReader((Stream)archive);
        CsvReader csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        IEnumerable<DataEntry> records = csv.GetRecords<DataEntry>();
        foreach (DataEntry entry in records) {
            dataTables[tableName].Add(entry.Key, entry);
        }
        csv.Dispose();
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
            throw new SFModDataRuntimeException($"Not a recipe file {uf.File}:{uf.type}");
        }

        string displayName = uf.GetDisplayName(false);
        UassetRecipe recipe = new UassetRecipe { UFile = uf, DisplayName = displayName };

        int defObjInd = uf.GetDefaultObjectIndex();

        recipe.ManufacturingDuration = uf.GetDouble(defObjInd, "Properties.mManufactoringDuration");
        recipe.VariablePowerConstant = uf.GetInt(defObjInd, "Properties.mVariablePowerConsumptionConstant");
        recipe.VariablePowerFactor = uf.GetInt(defObjInd, "Properties.mVariablePowerConsumptionFactor");

        bool hasActualMachine = false;
        bool isMachineRecipe = false;

        foreach (string assetPathName in uf.GetProducedIn()) {
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
                if (recipe.ProducedIn == null) {
                    recipe.ProducedIn = new HashSet<UAssetMachine>();
                }

                // build file
                UassetFile mchF = prov.NormalizeAndMatchPath(assetPathName);
                if (mchF.type != UassetType.MachineBuild) {
                    throw new SFModDataRuntimeException($"Not a machine build file {mchF.File}:{mchF.type}");
                }
                UAssetMachine uMch;
                if (prov.FileToMachine.ContainsKey(mchF.File)) {
                    uMch = prov.FileToMachine[mchF.File];
                }
                else {
                    string machineName = mchF.GetDisplayName();
                    uMch = new UAssetMachine { UFile = mchF, DisplayName = machineName };
                    prov.FileToMachine.Add(mchF.File, uMch);
                }
                
                recipe.ProducedIn.Add(uMch);
            }
            else {
                continue;
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

        JToken? ingredientsToken = uf.GetToken(defObjInd, "Properties.mIngredients");
        if (ingredientsToken != null) {
            foreach (JToken ingToken in ingredientsToken.Children()) {
                string? objPath = ingToken.SelectToken("ItemClass.ObjectPath")?.ToString();
                string? amount = ingToken.SelectToken("Amount")?.ToString();
                if (objPath != null && amount != null) {
                    UassetFile ingF = prov.NormalizeAndMatchPath(objPath);
                    SetupPart(ingF);

                    recipe.Ingredients.Add((int.Parse(amount), prov.FileToPart[ingF.File]));
                }
            }
        }

        // if the recipe is not named use a product name
        if (recipe.DisplayName == uf.GetString(0, "Name") && recipe.Products.Count() > 0) {
            recipe.DisplayName = recipe.Products.First().Item2.DisplayName;
        }

        prov.FileToRecipe.Add(uf.File, recipe);
    }

    private void SetupMachine(UAssetMachine machine) {

        // build file stuff
        int defObjInd = machine.UFile.GetDefaultObjectIndex();
        // the previous name was from Desc file but the build file generally has a more accurate name
        // mods tend to reference base game stuff that messes up naming so only search superclass if in a mod
        machine.DisplayName = machine.UFile.GetDisplayName(machine.UFile.Mod == "FactoryGame");
        machine.PowerConsumption = machine.UFile.GetInt(defObjInd, "Properties.mPowerConsumption");
        machine.PowerConsumptionExponent = machine.UFile.GetDouble(defObjInd, "Properties.mPowerConsumptionExponent");
        machine.ProductionShardSlotSize = machine.UFile.GetInt(defObjInd, "Properties.mProductionShardSlotSize");
        machine.ProductionShardBoostMultiplier = machine.UFile.GetDouble(defObjInd, "Properties.mProductionShardBoostMultiplier");
        machine.BasePowerProduction = machine.UFile.GetInt(defObjInd, "Properties.mBasePowerProduction");
        machine.BaseBoostPercentage = machine.UFile.GetDouble(defObjInd, "Properties.mBaseBoostPercentage");
        
        string? fuelClass = machine.UFile.GetString(defObjInd, "Properties.mDefaultFuelClasses.AssetPathname");
        if (fuelClass != null) {
            UassetFile fuelClassFile = prov.NormalizeAndMatchPath(fuelClass);
            int fuelClassDefObjInd = fuelClassFile.GetDefaultObjectIndex();
            machine.BoostPercentage  = fuelClassFile.GetDouble(fuelClassDefObjInd, "Properties.mBoostPercentage");
        }

        if (prov.FileToBuildingRecipe.ContainsKey(machine.UFile.File)) {
            UassetFile machineRecipeFile = prov.FileToBuildingRecipe[machine.UFile.File];
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
                            throw new SFModDataRuntimeException($"Couldn't get icon path for {machineDescFile.File}");
                        }
                        UassetFile iconUFile = prov.NormalizeAndMatchPath(iconObjPath);
                        machine.Icon = GetTexture(iconUFile, SFModUtility.GetAssetPathIndex(iconObjPath));
                    }
                }
            }
        }
    }


    public void SetupPart(UassetFile uf) {
        if (prov.FileToPart.ContainsKey(uf.File)) {
            return;
        }

        if (uf.type != UassetType.ItemDesc) {
            throw new SFModDataRuntimeException($"Not a part file {uf.File}:{uf.type}");
        }

        string displayName = uf.GetDisplayName();
        UassetPart part = new UassetPart { UFile = uf, DisplayName = displayName };

        int defObjInd = uf.GetDefaultObjectIndex();

        string? iconObjPath = uf.GetString(defObjInd, "Properties.mSmallIcon.ObjectPath");
        if (iconObjPath == null) {
            iconObjPath = uf.GetString(defObjInd, "Properties.mPersistentBigIcon.ObjectPath");
        }
        if (iconObjPath == null) {
            throw new SFModDataRuntimeException($"Couldn't get icon path for {uf.File}");
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
            throw new SFModDataRuntimeException($"Not a texture file {textureFile.File}:{textureFile.type}");
        }

        UObject uObj = textureFile.GetUObject(objIndex);
        if (uObj is not UTexture) {
            throw new SFModDataRuntimeException($"Not a texture object {textureFile.File}");
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
            throw new SFModDataRuntimeException($"Could not extract bitmaps {textureFile.File}");
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
        foreach (string csvFile in prov.CsvFiles) {
            prov.ReadCsv(csvFile, Path.GetFileNameWithoutExtension(csvFile));
        }

        foreach ((string modName, HashSet<UassetFile> modFiles) in prov.FilesByMod) {
            foreach (UassetFile uf in modFiles) {
                if (uf.type == UassetType.RecipeDesc) {
                    SetupRecipe(uf);
                }
            }
        }

        // machine setup is delayed so that machine recipes are already found
        foreach (UAssetMachine machine in prov.FileToMachine.Values) {
            SetupMachine(machine);
        }

        Console.WriteLine($"Recipes {prov.FileToRecipe.Count}");
        Console.WriteLine($"Parts {prov.FileToPart.Count}");
        Console.WriteLine($"Machines {prov.FileToMachine.Count}");
        Console.WriteLine($"MachineRecipes {prov.FileToBuildingRecipe.Count}");

        foreach ((string modName, HashSet<UassetFile> modFiles) in prov.FilesByMod) {
            List<(string, SKBitmap[])> iconsToSave = new List<(string, SKBitmap[])>();
            GameData modGameData = new GameData {
                Machines = new HashSet<GameDataMachine>(),
                MultiMachines = new HashSet<GameDataMultiMachine>(),
                Parts = new HashSet<GameDataItem>(),
                Recipes = new HashSet<GameDataRecipe>()
            };
            int recipesCount = 0, machinesCount = 0, partsCount = 0;

            foreach (UassetFile uf in modFiles) {
                if (prov.FileToRecipe.ContainsKey(uf.File)) {
                    // add recipe for each machine it can be produced in
                    foreach (GameDataRecipe rec in prov.FileToRecipe[uf.File].ToGameDataRecipe()) {
                        recipesCount++;
                        modGameData.Recipes.Add(rec);
                    }
                    // include one level of dependency for base game items that are not normall included
                    // like a recipe to produce hard drives
                    foreach ((_, UassetPart part) in prov.FileToRecipe[uf.File].Products) {
                        partsCount++;
                        modGameData.Parts.Add(part.ToGameDataItem());
                        SKBitmap[]? partIcon = part.Icon;
                        if (partIcon != null) {
                            iconsToSave.Add((part.DisplayName, partIcon));
                        }
                    }

                    foreach ((_, UassetPart part) in prov.FileToRecipe[uf.File].Ingredients) {
                        partsCount++;
                        modGameData.Parts.Add(part.ToGameDataItem());
                        SKBitmap[]? partIcon = part.Icon;
                        if (partIcon != null) {
                            iconsToSave.Add((part.DisplayName, partIcon));
                        }
                    }
                }
                else if (prov.FileToMachine.ContainsKey(uf.File)) {
                    machinesCount++;
                    modGameData.Machines.Add(prov.FileToMachine[uf.File].ToGameDataMachine());
                    SKBitmap[]? mchIcon = prov.FileToMachine[uf.File].Icon;
                    if (mchIcon != null) {
                        iconsToSave.Add((prov.FileToMachine[uf.File].DisplayName, mchIcon));
                    }
                }
                else if (prov.FileToPart.ContainsKey(uf.File)) {
                    partsCount++;
                    modGameData.Parts.Add(prov.FileToPart[uf.File].ToGameDataItem());
                    SKBitmap[]? partIcon = prov.FileToPart[uf.File].Icon;
                    if (partIcon != null) {
                        iconsToSave.Add((prov.FileToPart[uf.File].DisplayName, partIcon));
                    }
                }
            }

            if (modGameData.Machines.Count() + modGameData.MultiMachines.Count() + modGameData.Parts.Count() + modGameData.Recipes.Count() != 0) {
                Console.WriteLine($"{modName} has:");
                Console.WriteLine($"\tRecipes {recipesCount}");
                Console.WriteLine($"\tMachines {machinesCount}");
                Console.WriteLine($"\tParts {partsCount}");
                Directory.CreateDirectory(modName);
                modGameData.WriteGameData(Path.Combine(modName, $"game_data_{modName}.json"));

                Directory.CreateDirectory(Path.Combine(modName, "icons"));
                foreach ((string iconName, SKBitmap[] iconData) in iconsToSave) {
                    string savePath = Path.Combine(modName, "icons", $"{iconName.Replace(" ", "_")}.png");
                    foreach (SKBitmap bitmap in iconData) {
                        SKData bytes = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                        if (!Path.Exists(savePath)) {
                            File.WriteAllBytes(savePath, bytes.ToArray());
                        }
                    }
                }
            }
        }
    }
}