using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using CUE4Parse.Compression;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.Encryption.Aes;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse_Conversion.Textures;
using SFModDataMerger;
using CUE4Parse.UE4.Readers;
using CsvHelper;
using System.Globalization;
using Newtonsoft.Json;

namespace SFModDataExtractor;

class SFModDataExtractorConfig {
    public string satisfactory_path = "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Satisfactory\\";
    public bool save_icons = true;
    public bool write_to_modeler_after_extracting = false;
    public string modeler_path = "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Satisfactory Modeler\\";
}

class SFModDataProvider {
    // based on satisfactory-dev/asset-http
    private EGame Version = EGame.GAME_UE5_3;

    private DefaultFileProvider? __provider = null;
    public DefaultFileProvider FileProvider {
        get {
            if (__provider == null) {
                string fileProviderPath = Path.Combine(config.satisfactory_path, "FactoryGame\\");
                if (!Path.Exists(fileProviderPath)) {
                    throw new Exception($"Invalid satisfactory path {fileProviderPath} does not exist");
                }

                // OodleHelper.DownloadOodleDll();
                OodleHelper.Initialize(Path.GetFullPath(Path.Combine("./", OodleHelper.OODLE_DLL_NAME)));
                __provider = new DefaultFileProvider(
                    directory: fileProviderPath,
                    searchOption: SearchOption.AllDirectories,
                    versions: new VersionContainer(Version, ETexturePlatform.DesktopMobile),
                    StringComparer.InvariantCulture
                );
                var mc = new FileUsmapTypeMappingsProvider(
                    Path.Combine(config.satisfactory_path, "CommunityResources\\FactoryGame.usmap")
                );
                __provider.MappingsContainer = mc;
                __provider.Initialize();
                __provider.SubmitKey(new FGuid(), new FAesKey(($"0x{new string('0', 64)}")));
                // __provider.LoadLocalization(ELanguage.English);
            }

            return __provider;
        }
    }
    public SFModDataExtractorConfig config;

    public Dictionary<string, UassetRecipe> FileToRecipe = new Dictionary<string, UassetRecipe>();
    public Dictionary<string, UassetFile> FileToBuildingRecipe = new Dictionary<string, UassetFile>();
    public Dictionary<string, UassetPart> FileToPart = new Dictionary<string, UassetPart>();
    public Dictionary<string, UAssetMachine> FileToMachine = new Dictionary<string, UAssetMachine>();
    public HashSet<UassetFile> AlreadyParsedFiles = new HashSet<UassetFile>();
    public Dictionary<string, HashSet<UassetFile>> FilesByMod = new Dictionary<string, HashSet<UassetFile>>();
    public HashSet<UassetFile> AllUassetFiles = new HashSet<UassetFile>();
    public HashSet<string> CsvFiles = new HashSet<string>();
    public Dictionary<string, UassetFile> AssetPathToFile = new Dictionary<string, UassetFile>();
    public Dictionary<string, Dictionary<string, CsvDataEntry>> csvDataTables = new Dictionary<string, Dictionary<string, CsvDataEntry>>();
    public Dictionary<UassetFile, SinkDataEntry> sinkDataTable = new Dictionary<UassetFile, SinkDataEntry>();
    public Dictionary<UassetFile, Dictionary<string, string>> stringDataTables = new Dictionary<UassetFile, Dictionary<string, string>>();

    public SFModDataProvider(SFModDataExtractorConfig config) {
        this.config = config;
    }

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
        try {
            result.defaultObjectIndexSearchOverride = SFModUtility.GetAssetPathIndex(assetPath);
        }
        catch (FormatException) {

        }

        return result;
    }

    public void ReadCsv(string path, string tableName) {
        csvDataTables.Add(tableName, new Dictionary<string, CsvDataEntry>());
        FArchive? archive = FileProvider.CreateReader(path);
        StreamReader reader = new StreamReader((Stream)archive);
        CsvReader csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        IEnumerable<CsvDataEntry> records = csv.GetRecords<CsvDataEntry>();
        foreach (CsvDataEntry entry in records) {
            csvDataTables[tableName].Add(entry.Key, entry);
        }
        csv.Dispose();
    }
}

class SFModDataExtract {
    private SFModDataProvider prov;

    public SFModDataExtract() {
        SFModDataExtractorConfig? config;
        try {
            string text = File.ReadAllText("config.json");
            config = JsonConvert.DeserializeObject<SFModDataExtractorConfig>(text);
        }
        catch (Exception ex) {
            throw new SFModDataRuntimeException($"Error parsing config: {ex.Message}", ex);
        }

        if (config == null) {
            throw new Exception("Failed to parse config, got null result.");
        }

        prov = new SFModDataProvider(config);

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

    public void SetupRecipe(UassetFile uf, string techTier) {
        if (prov.FileToRecipe.ContainsKey(uf.File)) {
            return;
        }
        if (uf.type != UassetType.RecipeDesc) {
            throw new SFModDataRuntimeException($"Not a recipe file {uf.File}:{uf.type}");
        }

        string displayName = uf.GetDisplayName(false);
        UassetRecipe recipe = new UassetRecipe { UFile = uf, DisplayName = displayName, Tier = techTier };

        recipe.ManufacturingDuration = uf.GetDouble("Properties.mManufactoringDuration");
        recipe.VariablePowerConstant = uf.GetInt("Properties.mVariablePowerConsumptionConstant");
        recipe.VariablePowerFactor = uf.GetInt("Properties.mVariablePowerConsumptionFactor");

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
                    uMch = new UAssetMachine { UFile = mchF, DisplayName = machineName, Tier = techTier };
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

        JToken? productsToken = uf.GetToken("Properties.mProduct");
        if (productsToken != null) {
            foreach (JToken prodToken in productsToken.Children()) {
                string? objPath = prodToken.SelectToken("ItemClass.ObjectPath")?.ToString();
                string? amount = prodToken.SelectToken("Amount")?.ToString();
                if (objPath != null && amount != null) {
                    UassetFile prodF = prov.NormalizeAndMatchPath(objPath);
                    SetupPart(prodF, techTier);
                    recipe.Products.Add((int.Parse(amount), prov.FileToPart[prodF.File]));
                }
            }
        }

        JToken? ingredientsToken = uf.GetToken("Properties.mIngredients");
        if (ingredientsToken != null) {
            foreach (JToken ingToken in ingredientsToken.Children()) {
                string? objPath = ingToken.SelectToken("ItemClass.ObjectPath")?.ToString();
                string? amount = ingToken.SelectToken("Amount")?.ToString();
                if (objPath != null && amount != null) {
                    UassetFile ingF = prov.NormalizeAndMatchPath(objPath);
                    SetupPart(ingF, techTier);
                    recipe.Ingredients.Add((int.Parse(amount), prov.FileToPart[ingF.File]));
                }
            }
        }

        // if the recipe is not named use a product name
        if (recipe.DisplayName == uf.GetString("Name", exportIndex: 0) && recipe.Products.Count() > 0) {
            recipe.DisplayName = recipe.Products.First().Item2.DisplayName;
        }

        prov.FileToRecipe.Add(uf.File, recipe);
    }

    private void SetupMachine(UAssetMachine machine) {

        // build file stuff
        machine.DisplayName = machine.UFile.GetDisplayName(machine.UFile.Mod == "FactoryGame");
        machine.PowerConsumption = machine.UFile.GetInt("Properties.mPowerConsumption");
        machine.PowerConsumptionExponent = machine.UFile.GetDouble("Properties.mPowerConsumptionExponent");
        machine.ProductionShardSlotSize = machine.UFile.GetInt("Properties.mProductionShardSlotSize");
        machine.ProductionShardBoostMultiplier = machine.UFile.GetDouble("Properties.mProductionShardBoostMultiplier");
        machine.BasePowerProduction = machine.UFile.GetInt("Properties.mBasePowerProduction");
        machine.BaseBoostPercentage = machine.UFile.GetDouble("Properties.mBaseBoostPercentage");

        string? fuelClass = machine.UFile.GetString("Properties.mDefaultFuelClasses.AssetPathname");
        if (fuelClass != null) {
            UassetFile fuelClassFile = prov.NormalizeAndMatchPath(fuelClass);
            machine.BoostPercentage = fuelClassFile.GetDouble("Properties.mBoostPercentage");
        }

        if (prov.FileToBuildingRecipe.ContainsKey(machine.UFile.File)) {
            UassetFile machineRecipeFile = prov.FileToBuildingRecipe[machine.UFile.File];

            JToken? ingredientsToken = machineRecipeFile.GetToken("Properties.mIngredients");
            if (ingredientsToken != null) {
                foreach (JToken ingToken in ingredientsToken.Children()) {
                    string? objPath = ingToken.SelectToken("ItemClass.ObjectPath")?.ToString();
                    string? amount = ingToken.SelectToken("Amount")?.ToString();
                    if (objPath != null && amount != null) {
                        UassetFile ingF = prov.NormalizeAndMatchPath(objPath);
                        SetupPart(ingF, machine.Tier);

                        machine.Ingredients.Add((int.Parse(amount), prov.FileToPart[ingF.File]));
                    }
                }
            }

            JToken? productsToken = machineRecipeFile.GetToken("Properties.mProduct");
            if (productsToken != null) {
                // there should only be one product in a machine recipe
                foreach (JToken prodToken in productsToken.Children()) {
                    string? objPath = prodToken.SelectToken("ItemClass.ObjectPath")?.ToString();
                    if (objPath != null) {
                        UassetFile machineDescFile = prov.NormalizeAndMatchPath(objPath);
                        string? iconObjPath = machineDescFile.GetString("Properties.mSmallIcon.ObjectPath");
                        if (iconObjPath == null) {
                            iconObjPath = machineDescFile.GetString("Properties.mPersistentBigIcon.ObjectPath");
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


    public void SetupPart(UassetFile uf, string? techTier) {
        if (prov.FileToPart.ContainsKey(uf.File)) {
            return;
        }

        if (uf.type != UassetType.ItemDesc) {
            throw new SFModDataRuntimeException($"Not a part file {uf.File}:{uf.type}");
        }

        string displayName = uf.GetDisplayName();
        UassetPart part = new UassetPart { UFile = uf, DisplayName = displayName, Tier = techTier };

        if (prov.sinkDataTable.ContainsKey(uf)) {
            part.SinkPoints = prov.sinkDataTable[uf].Points;
        }

        string? formString = uf.GetString("Properties.mForm");
        part.Form = formString switch {
            "EResourceForm::RF_LIQUID" => ItemForm.Liquid,
            "EResourceForm::RF_GAS" => ItemForm.Gas,
            _ => ItemForm.Solid,
        };

        string? iconObjPath = uf.GetString("Properties.mSmallIcon.ObjectPath");
        if (iconObjPath == null) {
            iconObjPath = uf.GetString("Properties.mPersistentBigIcon.ObjectPath");
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
    }

    // recipe file -> desc file -> build file (save the build file so that it can be used to look up recipe file later)
    public void SetupMachineRecipe(UassetFile machineRecipeFile) {
        JToken? productsToken = machineRecipeFile.GetToken("Properties.mProduct");
        if (productsToken != null) {
            // there should only be one product in a machine recipe
            foreach (JToken prodToken in productsToken.Children()) {
                string? objPath = prodToken.SelectToken("ItemClass.ObjectPath")?.ToString();
                if (objPath != null) {
                    UassetFile machineDescFile = prov.NormalizeAndMatchPath(objPath);
                    string? buildFilePath = machineDescFile.GetString("Properties.mBuildableClass.ObjectPath");
                    if (buildFilePath != null) {
                        UassetFile machineBuildFile = prov.NormalizeAndMatchPath(buildFilePath);
                        prov.FileToBuildingRecipe.TryAdd(machineBuildFile.File, machineRecipeFile);
                        return;
                    }
                }
            }
        }
    }

    public void ParseSchematic(UassetFile uf) {
        if (prov.AlreadyParsedFiles.Contains(uf)) {
            return;
        }
        prov.AlreadyParsedFiles.Add(uf);

        int? majorTier = uf.GetInt("Properties.mTechTier");
        int? menuPrio = (int?)uf.GetDouble("Properties.mMenuPriority");
        string techTier = $"{majorTier ?? 1}-{menuPrio ?? 0}";

        JToken? unlocks = uf.GetToken("Properties.mUnlocks");
        if (unlocks != null) {
            foreach (JToken schemToken in unlocks.Children()) {
                string? objPath = schemToken.SelectToken("ObjectPath")?.ToString();
                if (objPath != null) {
                    // should be in the same file but lets do this to be sure

                    int unlockObjIndex = SFModUtility.GetAssetPathIndex(objPath);
                    UassetFile unlockFile = prov.NormalizeAndMatchPath(objPath);
                    JToken? recipesToken = unlockFile.GetToken("Properties.mRecipes", exportIndex: unlockObjIndex);
                    if (recipesToken != null) {
                        foreach (JToken recObjPathToken in recipesToken.Children()) {
                            string? recObjPath = recObjPathToken.SelectToken("ObjectPath")?.ToString();
                            if (recObjPath != null) {
                                UassetFile recipeFile = prov.NormalizeAndMatchPath(recObjPath);
                                SetupRecipe(recipeFile, techTier);
                            }
                        }
                    }
                }
            }
        }
    }

    public void ReadSinkPoints(UassetFile uf) {
        if (uf.type != UassetType.DataTable) {
            throw new SFModDataRuntimeException($"Not a data table {uf.File}");
        }
        JToken? rows = uf.GetToken("Rows", exportIndex: 0);
        if (rows == null) {
            return;
        }

        foreach (JToken entryToken in rows.Children().Values()) {
            string? objectName = entryToken.SelectToken("ItemClass.ObjectName")?.Value<string>();
            string? objectPath = entryToken.SelectToken("ItemClass.ObjectPath")?.Value<string>();
            int? points = entryToken.SelectToken("Points")?.Value<int>();
            int? overrideSinkPoints = entryToken.SelectToken("OverriddenResourceSinkPoints")?.Value<int>();
            if (objectPath != null && objectPath != "" && points != null) {
                UassetFile itemFile = prov.NormalizeAndMatchPath(objectPath);
                prov.sinkDataTable.Add(itemFile, new SinkDataEntry {
                    Key = objectPath,
                    ObjectName = objectName,
                    ObjectPath = objectPath,
                    Points = points,
                    OverriddenResourceSinkPoints = overrideSinkPoints,
                });
            }
        }
    }

    public void ParseResearchTree(UassetFile uf) {
        if (prov.AlreadyParsedFiles.Contains(uf)) {
            return;
        }
        prov.AlreadyParsedFiles.Add(uf);

        JToken? nodesToken = uf.GetToken("Properties.mNodes");
        if (nodesToken != null) {
            foreach (JToken nodeToken in nodesToken.Children()) {
                string? nodeObjPath = nodeToken.SelectToken("ObjectPath")?.ToString();
                if (nodeObjPath == null) {
                    continue;
                }
                // maybe it could be a different file
                UassetFile nodeFile = prov.NormalizeAndMatchPath(nodeObjPath);
                int nodeObjIndex = SFModUtility.GetAssetPathIndex(nodeObjPath);
                JToken? dataStructToken = nodeFile.GetToken("Properties.mNodeDataStruct", exportIndex: nodeObjIndex);
                if (dataStructToken == null) {
                    continue;
                }
                JToken? schemFileObjPath = null;
                foreach (JToken dataStructTokenChild in dataStructToken.Children().Values()) {
                    schemFileObjPath = dataStructTokenChild.SelectToken("ObjectPath");
                    if (schemFileObjPath != null) {
                        break;
                    }
                }

                if (schemFileObjPath == null || schemFileObjPath.ToString() == "") {
                    continue;
                }

                UassetFile schemFile = prov.NormalizeAndMatchPath(schemFileObjPath.ToString());
                ParseSchematic(schemFile);
            }
        }
    }

    public void ParseGameWorld(UassetFile uf) {
        string? sinkPointsPath = uf.GetString("Properties.mResourceSinkItemPointsTable.AssetPathName");
        if (sinkPointsPath != null) {
            UassetFile sinkPointsFile = prov.NormalizeAndMatchPath(sinkPointsPath);
            ReadSinkPoints(sinkPointsFile);
        }

        JToken? schematicsToken = uf.GetToken("Properties.mSchematics");
        if (schematicsToken != null) {
            foreach (JToken schemToken in schematicsToken.Children()) {
                string? objPath = schemToken.SelectToken("ObjectPath")?.ToString();
                if (objPath != null) {
                    UassetFile schemFile = prov.NormalizeAndMatchPath(objPath);
                    ParseSchematic(schemFile);
                }
            }
        }

        JToken? researchTreesToken = uf.GetToken("Properties.mResearchTrees");
        if (researchTreesToken != null) {
            foreach (JToken researchTreeToken in researchTreesToken.Children()) {
                string? objPath = researchTreeToken.SelectToken("ObjectPath")?.ToString();
                if (objPath != null) {
                    UassetFile researchTreeFile = prov.NormalizeAndMatchPath(objPath);
                    ParseResearchTree(researchTreeFile);
                }
            }
        }
    }

    public void SetupResourceNode(UassetFile uf) {
        if (prov.AlreadyParsedFiles.Contains(uf)) {
            return;
        }
        prov.AlreadyParsedFiles.Add(uf);

        string? resourceClassPath = uf.GetString("Properties.mResourceClass.ObjectPath");
        if (resourceClassPath != null) {
            UassetFile resourceClass = prov.NormalizeAndMatchPath(resourceClassPath);

            SetupPart(resourceClass, "0-0");
            UassetPart resourcePart = prov.FileToPart[resourceClass.File];

            UassetRecipe resourceRecipe = new UassetRecipe { UFile = uf, DisplayName = resourcePart.DisplayName, ManufacturingDuration = 60, Tier = "0-0" };
            resourceRecipe.ProducedIn =
            [
                new UAssetMachine { DisplayName = "Miner", UFile = prov.NormalizeAndMatchPath("FactoryGame/Content/FactoryGame/Buildable/Factory/MinerMK1/Build_MinerMk1.uasset") },
            ];

            int? extractMultiplier = uf.GetInt("Properties.mExtractMultiplier");
            resourceRecipe.Products = [(extractMultiplier ?? 1, resourcePart)];

            prov.FileToRecipe.Add(uf.File, resourceRecipe);
        }
    }

    public void doTheThing() {
        foreach (string csvFile in prov.CsvFiles) {
            prov.ReadCsv(csvFile, Path.GetFileNameWithoutExtension(csvFile));
        }

        // base game stuff since I couldn't find any gameworld file for the base game
        ReadSinkPoints(prov.NormalizeAndMatchPath("FactoryGame/Content/FactoryGame/Buildable/Factory/ResourceSink/DT_ResourceSinkPoints.uasset"));
        foreach (UassetFile baseSchemFile in prov.FilesByMod["FactoryGame"].Where(uf => uf.type == UassetType.Schematic)) {
            ParseSchematic(baseSchemFile);
        }

        foreach ((string modName, HashSet<UassetFile> modFiles) in prov.FilesByMod) {
            foreach (UassetFile uf in modFiles) {
                if (uf.type == UassetType.GameWorldModule) {
                    ParseGameWorld(uf);
                }
                else if (uf.type == UassetType.ResoureceNode) {
                    SetupResourceNode(uf);
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
        Console.WriteLine($"BuildableRecipes {prov.FileToBuildingRecipe.Count}");

        GameData baseGameData = GameData.ReadGameData("game_data_base.json");
        Dictionary<string, GameData> modGameDataDic = new Dictionary<string, GameData>();
        Dictionary<string, List<(string, SKBitmap[])>> modIconsDic = new Dictionary<string, List<(string, SKBitmap[])>>();

        foreach ((string modName, HashSet<UassetFile> modFiles) in prov.FilesByMod) {
            List<(string, SKBitmap[])> iconsToSave = new List<(string, SKBitmap[])>();
            modIconsDic.Add(modName, iconsToSave);
            GameData modGameData = new GameData {
                Machines = new HashSet<GameDataMachine>(),
                MultiMachines = new HashSet<GameDataMultiMachine>(),
                Parts = new HashSet<GameDataItem>(),
                Recipes = new HashSet<GameDataRecipe>()
            };
            modGameDataDic.Add(modName, modGameData);

            foreach (UassetFile uf in modFiles) {
                if (prov.FileToRecipe.ContainsKey(uf.File)) {
                    // add recipe for each machine it can be produced in
                    foreach (GameDataRecipe rec in prov.FileToRecipe[uf.File].ToGameDataRecipe()) {
                        if (baseGameData.Recipes.Contains(rec) && modName != "FactoryGame") {
                            rec.Name = SFModUtility.IncrementAltRecipeName(rec.Name);
                        }
                        while (!modGameData.Recipes.Add(rec)) {
                            rec.Name = SFModUtility.IncrementAltRecipeName(rec.Name);
                        }

                        foreach ((_, UassetPart part) in prov.FileToRecipe[uf.File].Products) {
                            if (part.UFile.Mod == "FactoryGame" && !baseGameData.Parts.Contains(part.ToGameDataItem())) {
                                modGameData.Parts.Add(part.ToGameDataItem());
                                if (part.Icon != null) {
                                    iconsToSave.Add((part.DisplayName, part.Icon));
                                }
                            }
                        }

                        foreach ((_, UassetPart part) in prov.FileToRecipe[uf.File].Ingredients) {
                            if (part.UFile.Mod == "FactoryGame" && !baseGameData.Parts.Contains(part.ToGameDataItem())) {
                                modGameData.Parts.Add(part.ToGameDataItem());
                                if (part.Icon != null) {
                                    iconsToSave.Add((part.DisplayName, part.Icon));
                                }
                            }
                        }
                    }
                }
                else if (prov.FileToMachine.ContainsKey(uf.File)) {
                    GameDataMachine mch = prov.FileToMachine[uf.File].ToGameDataMachine();
                    if (baseGameData.Machines.Contains(mch) && modName != "FactoryGame") {
                        mch.Name = SFModUtility.IncrementAltRecipeName(mch.Name);
                    }
                    while (!modGameData.Machines.Add(mch)) {
                        mch.Name = SFModUtility.IncrementAltRecipeName(mch.Name);
                    }
                    SKBitmap[]? mchIcon = prov.FileToMachine[uf.File].Icon;
                    if (mchIcon != null) {
                        iconsToSave.Add((mch.Name, mchIcon));
                    }

                    foreach ((_, UassetPart part) in prov.FileToMachine[uf.File].Ingredients) {
                        if (part.UFile.Mod == "FactoryGame" && !baseGameData.Parts.Contains(part.ToGameDataItem())) {
                            modGameData.Parts.Add(part.ToGameDataItem());
                            if (part.Icon != null) {
                                iconsToSave.Add((part.DisplayName, part.Icon));
                            }
                        }
                    }
                }
                else if (prov.FileToPart.ContainsKey(uf.File)) {
                    GameDataItem part = prov.FileToPart[uf.File].ToGameDataItem();
                    if (baseGameData.Parts.Contains(part) && modName != "FactoryGame") {
                        part.Name = SFModUtility.IncrementAltRecipeName(part.Name);
                    }
                    while (!modGameData.Parts.Add(part)) {
                        part.Name = SFModUtility.IncrementAltRecipeName(part.Name);
                    }
                    SKBitmap[]? partIcon = prov.FileToPart[uf.File].Icon;
                    if (partIcon != null) {
                        iconsToSave.Add((prov.FileToPart[uf.File].DisplayName, partIcon));
                    }
                }
            }

            if (modGameData.Machines.Count() + modGameData.MultiMachines.Count() + modGameData.Parts.Count() + modGameData.Recipes.Count() != 0) {
                if (modName == "FactoryGame") {
                    modGameData.Recipes = modGameData.Recipes.Where(r => !baseGameData.Recipes.Contains(r)).ToHashSet();
                    modGameData.Machines = modGameData.Machines.Where(m => !baseGameData.Machines.Contains(m)).ToHashSet();
                    modGameData.Parts = modGameData.Parts.Where(p => !baseGameData.Parts.Contains(p)).ToHashSet();
                }

                Console.WriteLine($"{modName} has:");
                Console.WriteLine($"\tRecipes {modGameData.Recipes.Count()}");
                Console.WriteLine($"\tMachines {modGameData.Machines.Count()}");
                Console.WriteLine($"\tParts {modGameData.Parts.Count()}");
                Directory.CreateDirectory(modName);
                modGameData.WriteGameData(Path.Combine(modName, $"game_data_{modName}.json"));

                if (prov.config.save_icons) {
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

        if (prov.config.write_to_modeler_after_extracting) {
            if (!Path.Exists(prov.config.modeler_path)) {
                throw new Exception($"Invalid modeler path {prov.config.modeler_path} does not exist");
            }

            GameData combinedGameData = new GameData {
                Machines = new HashSet<GameDataMachine>(),
                MultiMachines = new HashSet<GameDataMultiMachine>(),
                Parts = new HashSet<GameDataItem>(),
                Recipes = new HashSet<GameDataRecipe>()
            };

            combinedGameData = combinedGameData.Union(baseGameData);

            foreach ((string modName, GameData modGameData) in modGameDataDic) {
                combinedGameData = combinedGameData.Union(modGameData);
            }

            combinedGameData.WriteGameData(Path.Combine(prov.config.modeler_path, "game_data", "game_data.json"), true);

            if (prov.config.save_icons) {
                string icon_folder_path = Path.Combine(prov.config.modeler_path, "images", "icons");
                foreach ((string modName, List<(string, SKBitmap[])> modIcons) in modIconsDic) {
                    foreach ((string iconName, SKBitmap[] iconData) in modIcons) {
                        string savePath = Path.Combine(icon_folder_path, $"{iconName.Replace(" ", "_")}.png");
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
}