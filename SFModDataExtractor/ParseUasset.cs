using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using CUE4Parse.Compression;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using CUE4Parse.UE4.Readers;
using CsvHelper;
using System.Globalization;
using Newtonsoft.Json;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Texture;
using SkiaSharp;
using CUE4Parse_Conversion.Sounds;
using CUE4Parse_Conversion.Textures;
using CUE4Parse.UE4.Assets;
using CUE4Parse.MappingsProvider;
using SFModDataMerger;

namespace SFModDataExtractor;

class ParseUassets {
    public DefaultFileProvider provider = new("D:\\EvenMoreSteamExtras\\steamapps\\common\\Satisfactory", SearchOption.AllDirectories, new VersionContainer(EGame.GAME_UE5_3, ETexturePlatform.DesktopMobile));
    public Dictionary<string, Mod> mods = new Dictionary<string, Mod>();
    public Dictionary<string, Dictionary<string, DataEntry>> dataTables = new Dictionary<string, Dictionary<string, DataEntry>>();
    public Dictionary<string, SinkPointsEntry> sinkPoints = new Dictionary<string, SinkPointsEntry>();
    // filename -> displayName
    public Dictionary<string, string> machineLookup = new Dictionary<string, string>();
    public Dictionary<string, string> itemLookup = new Dictionary<string, string>();

    public void ReadCsv(string path, string tableName) {
        dataTables.Add(tableName, new Dictionary<string, DataEntry>());
        FArchive? archive = provider.CreateReader(path);
        StreamReader reader = new StreamReader((Stream)archive);
        CsvReader csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        IEnumerable<DataEntry> records = csv.GetRecords<DataEntry>();
        foreach (DataEntry entry in records) {
            dataTables[tableName].Add(entry.Key, entry);
        }
        csv.Dispose();
    }

    public void ReadSinkPoints(string path) {
        JToken sinkPointsUasset = JToken.FromObject(provider.LoadPackage(path).GetExports());
        JToken? sinkPointEntries = sinkPointsUasset.SelectToken("[0].Rows");
        if (sinkPointEntries == null) {
            return;
        }
        foreach (JProperty entry in sinkPointEntries) {
            string name = entry.Name;
            JToken? itemClass = entry.Value["ItemClass"];
            string? objectName = null;
            string? objectPath = null;
            if (itemClass != null && itemClass.HasValues) {
                objectName = itemClass?["ObjectName"]?.Value<string>() ?? "";
                objectPath = itemClass?["ObjectPath"]?.Value<string>() ?? "";
            }
            sinkPoints.Add(name, new SinkPointsEntry {
                Key = name,
                Points = entry.Value["Points"]?.Value<int>() ?? 0,
                ObjectName = objectName,
                ObjectPath = objectPath,
            });
        }
    }

    public ParseUassets() {
        OodleHelper.DownloadOodleDll();
        OodleHelper.Initialize(OodleHelper.OODLE_DLL_NAME);
        provider.Initialize();
        provider.Mount();

        ReadCsv("FactoryGame/Content/Localization/StringTables/Items_Data.csv", "Items_Data");
        ReadCsv("FactoryGame/Content/Localization/StringTables/Buildables_Data.csv", "Buildables_Data");
        ReadCsv("FactoryGame/Content/Localization/StringTables/Equipment_Data.csv", "Equipment_Data");
        ReadSinkPoints("FactoryGame/Content/FactoryGame/Buildable/Factory/ResourceSink/DT_ResourceSinkPoints.uasset");

        mods.Add("FactoryGame", new Mod { Name = "FactoryGame", Path = $"/Game/FactoryGame/" });
        foreach (string file in provider.Files.Keys) {
            if (file.EndsWith(".uasset") && file.StartsWith("FactoryGame")) {
                if (file.Contains("Mods")) {
                    Match nameMatch = Regex.Match(file, @"^FactoryGame/Mods/(\w+)/");
                    string name = nameMatch.Groups[1].Value;
                    mods.TryAdd(name, new Mod { Name = name, Path = $"FactoryGame/Mods/{name}/" });
                    mods[name].Files.Add(file);
                }
                else {
                    mods["FactoryGame"].Files.Add(file);
                }
            }
        }

        foreach (KeyValuePair<string, Mod> mp in mods) {
            mp.Value.Files = mp.Value.Files.OrderByDescending(f => f.Length).ToList();
            foreach (string file in mp.Value.Files) {
                if (TryGetRecipe(mp.Key, file)) {
                    continue;
                }
            }
        }
        foreach (KeyValuePair<string, Mod> mp in mods) {
            foreach (string file in mp.Value.Files) {
                if (TryGetMachine(mp.Key, file)) {
                    continue;
                }
            }
        }

        foreach (KeyValuePair<string, Mod> mp in mods) {
            // Console.WriteLine(mp.Value);
            foreach (KeyValuePair<string, Recipe> rc in mp.Value.Recipes) {
                if (!TryResolveMachines(rc.Value)) {
                    // Console.WriteLine(rc.Value);
                }
            }
        }

        foreach (KeyValuePair<string, Mod> mp in mods) {
            // Console.WriteLine(mp.Value);
            foreach (KeyValuePair<string, Recipe> rc in mp.Value.MachineRecipes) {
                if (!TryMatchWithMachine(rc.Value)) {
                    // Console.WriteLine(rc.Value);
                }
            }
        }

        foreach (KeyValuePair<string, Mod> mp in mods) {
            // Console.WriteLine(mp.Value);
            foreach (KeyValuePair<string, Recipe> rc in mp.Value.Recipes) {
                if (!TryResolveItems(rc.Value)) {
                    // Console.WriteLine(rc.Value);
                }
            }
        }
        // Console.WriteLine(JsonConvert.SerializeObject(machineLookup, Formatting.Indented));
        foreach (KeyValuePair<string, Mod> mp in mods) {
            File.WriteAllText($"moddata/{mp.Key.Replace(' ', '_')}.json", JsonConvert.SerializeObject(new {
                Machines = mp.Value.Machines.Values.Select(m => m.ToGameData()).Where(m => m.AveragePower != null || m.OverclockPowerExponent != null || m.MaxProductionShards != null || m.ProductionShardMultiplier != null),
                MultiMachines = new List<GameDataMultiMachine>(),
                Parts = mp.Value.Items.Values.Select(p => p.ToGameData()),
                Recipes = mp.Value.Recipes.Values.Select(rl => rl.ToGameData()).SelectMany(r => r),
            }, Formatting.Indented, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
        }
    }

    public string? FindString(JToken token, string tokenPath) {
        JToken? result = FindToken(token, tokenPath);
        if (result?.Type is JTokenType.String) {
            return result.ToString();
        }
        return null;
    }

    public JProperty? FindProperty(JToken token, string tokenPath) {
        JToken? result = FindToken(token, tokenPath);
        if (result?.Type is JTokenType.Property) {
            return (JProperty)result;
        }
        return null;
    }

    public JToken? FindToken(JToken token, string tokenPath) {
        JToken? result;
        foreach (JToken subToken in token) {
            result = subToken.SelectToken(tokenPath);
            if (result != null && result.ToString() != "") {
                return result;
            }
        }
        return null;
    }

    public string? NormalizeAssetPath(string objectPath) {
        // remove ".x" and add ".uasset"
        string? dirPortion = Path.GetDirectoryName(objectPath);
        if (dirPortion == null) {
            return null;
        }
        string assetPath = Path.Combine(dirPortion, Path.GetFileNameWithoutExtension(objectPath) + ".uasset").Replace('\\', '/');
        // remove mod name from the start
        List<string> parts = assetPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToList();
        if (parts.First() == "Game") {
            parts.RemoveAt(0);
        }
        string? mod = parts.FirstOrDefault(n => mods.ContainsKey(n));
        if (mod == null) {
            return null;
        }
        parts.Remove(mod);
        string minPath = Path.Combine(parts.ToArray()).Replace('\\', '/');
        // search for a match in mod filename list (as postfix)
        return mods[mod].Files.FirstOrDefault(f => f.EndsWith(minPath, StringComparison.OrdinalIgnoreCase));
    }

    public bool isRecipe(JToken token) {
        string? superStruct = FindStringRecursive(token, "SuperStruct.ObjectName");
        if (superStruct != null && superStruct == "Class'FGRecipe'") {
            return true;
        }
        return false;
    }

    public JToken? FindTokenRecursive(JToken token, string path, bool debug = false) {
        JToken? fToken = FindToken(token, path);
        if (fToken != null && fToken.ToString() != "") {
            if (debug) {
                Console.WriteLine($"Returning result for {path} {fToken.ToString()}");
            }
            return fToken;
        }

        string? super = FindString(token, "Super.ObjectPath");
        if (super != null && super != "") {
            string? filePath = NormalizeAssetPath(super);
            if (filePath == null) {
                return null;
            }
            if (debug) {
                Console.WriteLine($"Opening alternate file for {path} {filePath}");
            }

            JToken newToken = JToken.FromObject(provider.LoadPackage(filePath).GetExports());
            return FindTokenRecursive(newToken, path, debug);
        }

        if (debug) {
            Console.WriteLine($"Failed to get {path} or alternate");
        }

        return null;
    }

    public string? FindStringRecursive(JToken token, string path, bool debug = false) {
        JToken? fToken = FindTokenRecursive(token, path, debug);
        if (fToken != null && fToken.Type == JTokenType.String) {
            return fToken.ToString();
        }
        if (debug) {
            Console.WriteLine($"Didn't find {path}");
        }
        return null;
    }

    public bool TryGetRecipe(string modName, string file) {
        JToken token = JToken.FromObject(provider.LoadPackage(file).GetExports());
        if (!isRecipe(token)) {
            return false;
        }

        string? displayName = FindStringRecursive(token, "Properties.mDisplayName.LocalizedString");
        string? nameTable = FindStringRecursive(token, "Properties.mDisplayName.TableId");
        string? nameKey = FindStringRecursive(token, "Properties.mDisplayName.Key");

        if (displayName == null && nameTable != null && nameKey != null && dataTables.ContainsKey(nameTable)) {
            displayName = dataTables[nameTable][nameKey].SourceString;
        }

        if (displayName == null) {
            displayName = FindStringRecursive(token, "Name");
        }

        if (displayName == null) {
            return false;
        }

        // trying to find recipe fields
        JToken? ingredients = FindTokenRecursive(token, "Properties.mIngredients");
        JToken? products = FindTokenRecursive(token, "Properties.mProduct");
        JToken? duration = FindTokenRecursive(token, "Properties.mManufactoringDuration");
        JToken? producedIn = FindTokenRecursive(token, "Properties.mProducedIn");

        Recipe recipe = new Recipe {
            DisplayName = displayName,
            File = file,
            Duration = duration?.Value<float>(),
        };

        if (ingredients != null) {
            foreach (JToken ing in ingredients) {
                JToken? itemClass = ing["ItemClass"];
                if (itemClass == null || !itemClass.HasValues) {
                    continue;
                }
                string? objectName = itemClass?["ObjectName"]?.Value<string>();
                string? objectPath = itemClass?["ObjectPath"]?.Value<string>();
                recipe.ItemReferences.Add(new RecipeItemReference {
                    isProduct = false,
                    Ammount = ing["Amount"]?.Value<int>(),
                    ObjectName = objectName,
                    ObjectPath = objectPath
                });
            }
        }

        if (products != null) {
            foreach (JToken prod in products) {
                JToken? itemClass = prod["ItemClass"];
                if (itemClass == null || !itemClass.HasValues) {
                    continue;
                }
                string objectName = itemClass?["ObjectName"]?.Value<string>() ?? "";
                string objectPath = itemClass?["ObjectPath"]?.Value<string>() ?? "";
                recipe.ItemReferences.Add(new RecipeItemReference {
                    isProduct = true,
                    Ammount = prod["Amount"]?.Value<int>() ?? 0,
                    ObjectName = objectName,
                    ObjectPath = objectPath
                });
            }
        }

        bool hasBuildGun = false;
        if (producedIn != null) {
            foreach (JToken mach in producedIn) {
                string assetPathName = mach["AssetPathName"]?.Value<string>() ?? "";
                string subPathString = mach["SubPathString"]?.Value<string>() ?? "";
                if (assetPathName == "/Game/FactoryGame/Equipment/BuildGun/BP_BuildGun.BP_BuildGun_C" || assetPathName == "/Script/FactoryGame.FGBuildGun") {
                    hasBuildGun = true;
                }
                if (assetPathName == "/Game/FactoryGame/Buildable/-Shared/WorkBench/BP_WorkshopComponent.BP_WorkshopComponent_C" ||
                    assetPathName == "/Game/FactoryGame/Buildable/-Shared/WorkBench/BP_WorkBenchComponent.BP_WorkBenchComponent_C" ||
                    assetPathName == "/Script/FactoryGame.FGBuildableAutomatedWorkBench") {
                    // skip workbench and workshop
                    continue;
                }
                if (assetPathName == "" && subPathString == "") {
                    continue;
                }
                recipe.MachineReferences.Add(new RecipeMachineReference {
                    AssetPathName = assetPathName,
                    SubPathString = subPathString,
                });
            }
        }

        if (hasBuildGun) {
            if (!mods[modName].MachineRecipes.TryAdd(displayName, recipe)) {
                return mods[modName].MachineRecipes.TryAdd(displayName + " Alternate", recipe);
            }
        }
        else {
            if (!mods[modName].Recipes.TryAdd(displayName, recipe)) {
                return mods[modName].Recipes.TryAdd(displayName + " Alternate", recipe);
            }
        }

        return true;
    }

    public bool isMachine(JToken token) {
        string? superStruct = FindStringRecursive(token, "SuperStruct.ObjectName");
        if (superStruct != null && superStruct == "Class'FGBuildingDescriptor'") {
            return true;
        }

        return false;
    }

    public bool TryGetMachine(string modName, string file) {
        JToken token = JToken.FromObject(provider.LoadPackage(file).GetExports());
        if (!isMachine(token)) {
            return false;
        }

        string? buildFilePath = FindStringRecursive(token, "Properties.mBuildableClass.ObjectPath");
        if (buildFilePath == null) {
            return false;
        }
        buildFilePath = NormalizeAssetPath(buildFilePath);
        if (buildFilePath == null) {
            return false;
        }
        JToken buildFileToken = JToken.FromObject(provider.LoadPackage(buildFilePath).GetExports());
        string? displayName = FindStringRecursive(buildFileToken, "Properties.mDisplayName.LocalizedString");
        string? nameTable = FindStringRecursive(buildFileToken, "Properties.mDisplayName.TableId");
        string? nameKey = FindStringRecursive(buildFileToken, "Properties.mDisplayName.Key");

        if (displayName == null && nameTable != null && nameKey != null && dataTables.ContainsKey(nameTable)) {
            displayName = dataTables[nameTable][nameKey].SourceString;
        }

        if (displayName == null) {
            displayName = FindStringRecursive(token, "Name");
        }

        if (displayName == null) {
            return false;
        }

        // trying to find machine fields
        float? averagePower = FindTokenRecursive(buildFileToken, "Properties.mPowerConsumption")?.Value<float>();
        float? overclockPowerExponent = FindTokenRecursive(buildFileToken, "Properties.mPowerConsumptionExponent")?.Value<float>();
        int? maxProductionShards = FindTokenRecursive(buildFileToken, "Properties.mProductionShardSlotSize")?.Value<int>();
        float? productionShardsMultiplier = FindTokenRecursive(buildFileToken, "Properties.mProductionShardBoostMultiplier")?.Value<float>();

        JToken? fallbackInfo = getFallbackPowerInfo(modName, buildFileToken);
        if (fallbackInfo != null) {
            if (averagePower == null) {
                averagePower = FindTokenRecursive(fallbackInfo, "Properties.mPowerConsumption")?.Value<float>();
            }
            if (overclockPowerExponent == null) {
                overclockPowerExponent = FindTokenRecursive(fallbackInfo, "Properties.mPowerConsumptionExponent")?.Value<float>();
            }
            if (maxProductionShards == null) {
                maxProductionShards = FindTokenRecursive(fallbackInfo, "Properties.mProductionShardSlotSize")?.Value<int>();
            }
            if (productionShardsMultiplier == null) {
                productionShardsMultiplier = FindTokenRecursive(fallbackInfo, "Properties.productionShardsMultiplier")?.Value<float>();
            }
        }

        Machine machine = new Machine {
            DisplayName = displayName,
            File = file,
            BuildFile = buildFilePath,
            AveragePower = averagePower,
            OverclockPowerExponent = overclockPowerExponent,
            MaxProductionShards = maxProductionShards,
            ProductionShardsMultiplier = productionShardsMultiplier,
        };

        string? iconPathRef = FindStringRecursive(token, "Properties.mSmallIcon.ObjectPath");
        if (iconPathRef != null) {
            SaveTexture(iconPathRef, $"icons/{displayName.Replace(" ", "_").Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_')}.png");
        }

        if (mods[modName].Machines.TryAdd(displayName, machine)) {
            machineLookup.TryAdd(buildFilePath, displayName);
            machineLookup.TryAdd(file, displayName);
        }
        else if (mods[modName].Machines.TryAdd(displayName + " Alternate", machine)) {
            machineLookup.TryAdd(buildFilePath, displayName + " Alternate");
            machineLookup.TryAdd(file, displayName + " Alternate");
        }
        else {
            return false;
        }

        return true;
    }

    public JToken? getFallbackPowerInfo(string modName, JToken buildFileToken) {
        // assuming power info is in the same file
        JToken? powerInfo = FindTokenRecursive(buildFileToken, "Properties.mPowerInfo.ObjectPath");
        if (powerInfo == null || powerInfo?.Type is not JTokenType.String) {
            return null;
        }

        string ext = Path.GetExtension(powerInfo.ToString()).Replace(".", "");
        JToken? linkedFilePath = buildFileToken.SelectToken($"[{ext}].Template.ObjectPath");
        if (linkedFilePath == null || linkedFilePath?.Type is not JTokenType.String) {
            return null;
        }

        string? linkedFile = NormalizeAssetPath(linkedFilePath.ToString());
        if (linkedFile == null) {
            return null;
        }

        JToken powerInfoToken = JToken.FromObject(provider.LoadPackage(linkedFile).GetExports());

        return powerInfoToken;
    }

    public bool TryGetPart(string modName, string file) {

        return false;
    }

    public bool TryResolveMachines(Recipe recipe) {
        bool hadFailure = false;
        foreach (RecipeMachineReference mchRef in recipe.MachineReferences) {
            if (mchRef.AssetPathName == null || mchRef.AssetPathName == "") {
                continue;
            }
            string? buildFileName = NormalizeAssetPath(mchRef.AssetPathName);
            if (buildFileName == null) {
                continue;
            }
            string? matchingMch = machineLookup.GetValueOrDefault(buildFileName);
            if (matchingMch == null) {
                hadFailure = true;
                continue;
            }
            recipe.ResolvedMachineReferences.Add(matchingMch);
        }
        return !hadFailure;
    }

    public string? getModFromFilename(string filename) {
        List<string> parts = filename.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToList();
        string? mod = parts.FirstOrDefault(n => mods.ContainsKey(n) && n != "FactoryGame");
        if (mod == null) {
            mod = parts.FirstOrDefault(n => n == "FactoryGame");
        }
        return mod;
    }

    public bool TryMatchWithMachine(Recipe recipe) {
        bool hadFailure = true;
        foreach (RecipeItemReference itmRef in recipe.ItemReferences) {
            if (!itmRef.isProduct) {
                continue;
            }
            if (itmRef.ObjectPath == null || itmRef.ObjectPath == "") {
                continue;
            }
            string? descFileName = NormalizeAssetPath(itmRef.ObjectPath);
            if (descFileName == null) {
                Console.WriteLine($"Failed normalize path {itmRef.ObjectPath}");
                continue;
            }
            string? matchingMch = machineLookup.GetValueOrDefault(descFileName);
            if (matchingMch == null) {
                // Console.WriteLine($"Failed to match mch {descFileName}");
                continue;
            }
            string? mod = getModFromFilename(descFileName);
            if (mod == null) {
                Console.WriteLine($"Failed to get mod {descFileName},{matchingMch}");
                continue;
            }
            hadFailure = false;
            // Console.WriteLine($"trying {mod},{matchingMch}");
            mods[mod].Machines[matchingMch].Recipe = recipe;
            break;
        }
        return !hadFailure;
    }

    // still fails a lot
    // could add a look up for Properties.mCategory and allow FGItemCategory?
    public bool isItem(JToken token) {
        string? superStruct = FindStringRecursive(token, "SuperStruct.ObjectName");
        if (superStruct != null && (superStruct.Contains("FGItemDescriptor") || superStruct.Contains("FGResourceDescriptor") || superStruct.Contains("FGConsumableDescriptor"))) {
            return true;
        }
        // Console.WriteLine($"Failed to get superStruct {superStruct}");
        return false;
    }

    public bool SaveTexture(string path, string savePath) {
        string? assetPath = NormalizeAssetPath(path);
        if (assetPath == null) {
            return false;
        }
        IPackage pack = provider.LoadPackage(assetPath);
        // Console.WriteLine(JsonConvert.SerializeObject(pack, Formatting.Indented));
        IEnumerable<UObject> exports = pack.GetExports();
        foreach (UObject obj in exports) {
            if (obj is not UTexture) {
                continue;
            }
            UTexture tex = (UTexture)obj;
            SKBitmap?[]? bitmaps = new[] { tex.Decode(ETexturePlatform.DesktopMobile) };
            switch (tex)
            {
                case UTexture2DArray textureArray:
                    bitmaps = textureArray.DecodeTextureArray(ETexturePlatform.DesktopMobile);
                    break;
                case UTextureCube:
                    bitmaps[0] = bitmaps[0]?.ToPanorama();
                    break;
            }
            if (bitmaps == null) {
                continue;
            }

            foreach (SKBitmap? bitmap in bitmaps) {
                if (bitmap is null) continue;
                SKData bytes = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                if (!Path.Exists(savePath)) {
                    File.WriteAllBytes(savePath, bytes.ToArray());
                }
            }
            return true;
        }
        return false;
    }

    public bool TryGetItem(string file) {
        JToken token = JToken.FromObject(provider.LoadPackage(file).GetExports());
        // if (!isItem(token)) {
        //     // Console.WriteLine($"Not item {file}");
        //     return false;
        // }

        string? displayName = FindStringRecursive(token, "Properties.mDisplayName.LocalizedString");
        string? nameTable = FindStringRecursive(token, "Properties.mDisplayName.TableId");
        string? nameKey = FindStringRecursive(token, "Properties.mDisplayName.Key");

        if (displayName == null && nameTable != null && nameKey != null && dataTables.ContainsKey(nameTable)) {
            displayName = dataTables[nameTable][nameKey].SourceString;
        }

        if (displayName == null) {
            displayName = FindStringRecursive(token, "Name");
        }

        if (displayName == null) {
            return false;
        }

        string? iconPathRef = FindStringRecursive(token, "Properties.mSmallIcon.ObjectPath");
        if (iconPathRef != null) {
            SaveTexture(iconPathRef, $"icons/{displayName.Replace(" ", "_")}.png");
        }

        if (!itemLookup.TryAdd(file, displayName)) {
            int count = 0;
            while (!itemLookup.TryAdd(file, $"{displayName} Alternate{count}") && count < 99) {
                count += 1;
            }
        }

        return true;
    }

    public bool TryResolveItems(Recipe recipe) {
        bool hadFailure = false;
        foreach (RecipeItemReference itmRef in recipe.ItemReferences) {
            if (itmRef.ObjectPath == null || itmRef.ObjectPath == "") {
                Console.WriteLine($"Failed to have objectPath {itmRef},{recipe.File}");
                continue;
            }
            string? itemFileName = NormalizeAssetPath(itmRef.ObjectPath);
            if (itemFileName == null) {
                Console.WriteLine($"Failed to get itemFileName {itmRef.ObjectPath},{recipe.File}");
                continue;
            }
            ResolvedRecipeItemReference rItmRef = new ResolvedRecipeItemReference {
                isProduct = itmRef.isProduct,
                Ammount = itmRef.Ammount,
            };

            if (!itemLookup.ContainsKey(itemFileName)) {
                if (!TryGetItem(itemFileName)) {
                    Console.WriteLine($"Failed to get item {itemFileName},{recipe.File}");
                    hadFailure = true;
                    continue;
                }
            }

            string? mod = getModFromFilename(itemFileName);
            if (mod == null) {
                Console.WriteLine($"Failed to get mod {itemFileName},{itemLookup[itemFileName]},{recipe.File}");
                continue;
            }
            mods[mod].Items.TryAdd(itemLookup[itemFileName], new Item { DisplayName = itemLookup[itemFileName], File = itemFileName });
            rItmRef.Name = itemLookup[itemFileName];
            recipe.ResolvedItemReferences.Add(rItmRef);
        }
        return !hadFailure;
    }
}