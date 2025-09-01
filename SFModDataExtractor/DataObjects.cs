using System.Linq.Expressions;
using System.Text.RegularExpressions;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using Fractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SFModDataMerger;
using SkiaSharp;
using ZstdSharp.Unsafe;

namespace SFModDataExtractor;

class StringDataEntry {
    public required string Key { get; set; }
    public string? SourceString { get; set; }
    public string? Context { get; set; }
    public string? VariableDescription { get; set; }
    public string? Type { get; set; }
    public string? Tag { get; set; }

    public override string ToString() {
        return $"StringDataEntry({SourceString},{SourceString},{Context},{VariableDescription},{Type},{Tag})";
    }
}

class SinkDataEntry {
    // this is the ObjectPath since RowName is often just trash
    public required string Key { get; set; }
    public string? RowName { get; set; }
    public string? ObjectName { get; set; }
    public string? ObjectPath { get; set; }
    public int? Points { get; set; }
    public int? OverriddenResourceSinkPoints { get; set; }
}

enum UassetType {
    RecipeDesc,
    MachineDesc,
    MachineBuild,
    ItemDesc,
    Schematic,
    ResearchTree,
    Texture,
    ResoureceNode,
    DataTable,
    GameWorldModule,
    Other
}

[Serializable]
class SFModDataRuntimeException : Exception {
    public SFModDataRuntimeException() { }
    public SFModDataRuntimeException(string message) : base(message) { }
    public SFModDataRuntimeException (string message, Exception innerException) : base (message, innerException) { }
}

class UassetFile : IComparable<UassetFile>, IEquatable<UassetFile> {
    public string File { get; set; }
    public string Mod { get; set; }
    private SFModDataProvider _provider;
    private List<JToken>? _exports;
    public List<JToken> exports {
        get {
            if (_exports == null) {
                _exports = new List<JToken>();
                foreach (UObject o in _provider.FileProvider.LoadPackage(File).GetExports()) {
                    _exports.Add(JToken.FromObject(o));
                }
            }

            return _exports;
        }
    }
    public UassetFile? _super;
    public UassetFile? super {
        get {
            if (_super == null) {
                JToken? superPathToken = exports[defaultObjectIndexSearchOverride].SelectToken("Super.ObjectPath");
                if (superPathToken == null || superPathToken.Type != JTokenType.String || superPathToken.ToString() == "") {
                    return null;
                }
                _super = _provider.NormalizeAndMatchPath(superPathToken.ToString());
            }
            return _super;
        }
    }
    public UassetType? _type;
    public UassetType type {
        get {
            if (_type == null) {
                string? objClass = GetString("Class", exportIndex: defaultObjectIndexSearchOverride);
                if (objClass != null) {
                    switch (objClass) {
                        case "UScriptClass'Texture2D'":
                            _type = UassetType.Texture;
                            return (UassetType)_type;
                        case "UScriptClass'DataTable'":
                            _type = UassetType.DataTable;
                            return (UassetType)_type;
                    }
                }
                if (super != null) {
                    _type = super.type;
                    return (UassetType)_type;
                }
                string? superStruct = GetString("SuperStruct.ObjectName", exportIndex: defaultObjectIndexSearchOverride);
                _type = superStruct switch {
                    "Class'FGRecipe'" => UassetType.RecipeDesc,
                    "Class'FGBuildableManufacturer'" => UassetType.MachineBuild,
                    "Class'FGBuildableManufacturerVariablePower'" => UassetType.MachineBuild,
                    "Class'FGBuildingDescriptor'" => UassetType.MachineDesc,
                    "Class'FGItemDescriptor'" => UassetType.ItemDesc,
                    "Class'FGResourceDescriptor'" => UassetType.ItemDesc,
                    "Class'FGPowerShardDescriptor'" => UassetType.ItemDesc,
                    "Class'FGItemDescriptorBiomass'" => UassetType.ItemDesc,
                    "Class'FGEquipmentDescriptor'" => UassetType.ItemDesc,
                    "Class'FGConsumableDescriptor'" => UassetType.ItemDesc,
                    "Class'FGAmmoTypeInstantHit'" => UassetType.ItemDesc,
                    "Class'FGAmmoTypeProjectile'" => UassetType.ItemDesc,
                    "Class'FGAmmoTypeSpreadshot'" => UassetType.ItemDesc,
                    "Class'FGItemDescriptorPowerBoosterFuel'" => UassetType.ItemDesc,
                    "Class'FGItemDescriptorNuclearFuel'" => UassetType.ItemDesc,
                    "Class'FGSchematic'" => UassetType.Schematic,
                    "Class'FGResearchTree'" => UassetType.ResearchTree,
                    "Class'FGResourceNode'" => UassetType.ResoureceNode,
                    _ => UassetType.Other,
                };

                if (_type == UassetType.Other) {
                    try {
                        bool? rootModule = GetBool("Properties.bRootModule");
                        JToken? schematics = GetToken("Properties.mSchematics");
                        if (rootModule == true && schematics != null) {
                            _type = UassetType.GameWorldModule;
                        }
                    }
                    catch (SFModDataRuntimeException) {
                    }
                }
            }
            return (UassetType)_type;
        }
    }
    public int defaultObjectIndexSearchOverride = 0;
    private int? _defaultObjectIndex;
    public int defaultObjectIndex {
        get {
            if (_defaultObjectIndex == null) {
                _defaultObjectIndex = GetDefaultObjectIndex();
            }
            return (int)_defaultObjectIndex;
        }
    }

    public UObject GetUObject(int objIndex) {
        return _provider.FileProvider.LoadPackage(File).GetExports().ToArray()[objIndex];
    }

    public JToken? GetToken(string tokenPath, int? exportIndex = null, bool searchSuper = true, bool forceSuper = false) {
        if (exportIndex == null) {
            exportIndex = defaultObjectIndex;
        }
        if (exportIndex >= exports.Count()) {
            throw new SFModDataRuntimeException($"Exports index ({exportIndex}) out of range ({exports.Count()} for {File})");
        }
        JToken? result = null;
        if (!forceSuper) {
            result = exports[(int)exportIndex].SelectToken(tokenPath);
        }
        if (result != null && result.ToString() != "") {
            return result;
        }
        if (searchSuper && super != null) {
            try {
                result = super.GetToken(tokenPath, searchSuper: true);
            }
            catch (SFModDataRuntimeException) {
                // Console.WriteLine($"Failed to get from super {File}->{super.File}");
            }
        }
        return result;
    }

    public string? GetString(string tokenPath, int? exportIndex = null, bool searchSuper = true, bool forceSuper = false) {
        JToken? result = GetToken(tokenPath, exportIndex, searchSuper, forceSuper);
        if (result?.Type is JTokenType.String) {
            return result.ToString();
        }
        return null;
    }

    public int? GetInt(string tokenPath, int? exportIndex = null, bool searchSuper = true, bool forceSuper = false) {
        JToken? result = GetToken(tokenPath, exportIndex, searchSuper, forceSuper);
        if (result?.Type is JTokenType.String || result?.Type is JTokenType.Float || result?.Type is JTokenType.Integer) {
            return int.Parse(result.ToString());
        }
        return null;
    }

    public double? GetDouble(string tokenPath, int? exportIndex = null, bool searchSuper = true, bool forceSuper = false) {
        JToken? result = GetToken(tokenPath, exportIndex, searchSuper, forceSuper);
        if (result?.Type is JTokenType.String || result?.Type is JTokenType.Float || result?.Type is JTokenType.Integer) {
            return double.Parse(result.ToString());
        }
        return null;
    }

    public bool? GetBool(string tokenPath, int? exportIndex = null, bool searchSuper = true, bool forceSuper = false) {
        JToken? result = GetToken(tokenPath, exportIndex, searchSuper, forceSuper);
        if (result?.Type is JTokenType.Boolean) {
            return result?.Value<bool>();
        }
        return null;
    }

    public string GetDisplayName(bool searchSuper = true) {
        // localized string can just be some very weird stuff in base game 
        // like the recipe for "Blue FICSMAS Ornament" being named "Iron Plate"
        // and cooling system bing named "Truck Chassis"
        string? displayName = (Mod == "FactoryGame") ? GetString("Name", exportIndex: defaultObjectIndexSearchOverride, searchSuper) : GetString("Properties.mDisplayName.LocalizedString", searchSuper: searchSuper);
        string? nameTable = GetString("Properties.mDisplayName.TableId", searchSuper: false);
        string? nameKey = GetString("Properties.mDisplayName.Key", searchSuper: false);
        if ((nameTable == null || nameKey == null) && (searchSuper)) {
            nameTable = GetString("Properties.mDisplayName.TableId", searchSuper: true, forceSuper: true);
            nameKey = GetString("Properties.mDisplayName.Key", searchSuper: true, forceSuper: true);
        }

        if (nameTable != null && nameKey != null && _provider.stringDataTables.ContainsKey(nameTable)) {
            if (!_provider.stringDataTables[nameTable].ContainsKey(nameKey)) {
                throw new SFModDataRuntimeException($"Table entry missing {nameTable}:{nameKey} in {File}");
            }
            else {
                displayName = _provider.stringDataTables[nameTable][nameKey].SourceString;
            }
        }

        if (displayName == null) {
            displayName = GetString("Name", exportIndex: defaultObjectIndexSearchOverride, searchSuper: searchSuper);
        }

        if (displayName == null) {
            throw new SFModDataRuntimeException($"Could not get name for {File}");
        }
        return displayName;
    }

    public IEnumerable<string> GetProducedIn(bool searchSuper = true) {
        JToken? producedInToken = GetToken("Properties.mProducedIn");
        if (producedInToken != null) {
            foreach (JToken mchToken in producedInToken.Children()) {
                string? assetPathName = mchToken.SelectToken("AssetPathName")?.ToString();
                if (assetPathName != null && assetPathName != "") {
                    yield return assetPathName;
                }
            }
        }
        producedInToken = GetToken("Properties.mProducedIn", searchSuper: searchSuper, forceSuper: true);
        if (producedInToken != null) {
            foreach (JToken mchToken in producedInToken.Children()) {
                string? assetPathName = mchToken.SelectToken("AssetPathName")?.ToString();
                if (assetPathName != null && assetPathName != "") {
                    yield return assetPathName;
                }
            }
        }
        yield break;
    }

    private int GetDefaultObjectIndex() {
        string? defObjPath = GetString("ClassDefaultObject.ObjectPath", exportIndex: defaultObjectIndexSearchOverride);
        if (defObjPath == null) {
            throw new SFModDataRuntimeException($"Couldn't get default object path for {File}");
        }
        int index = SFModUtility.GetAssetPathIndex(defObjPath);
        if (index >= exports.Count()) {
            throw new SFModDataRuntimeException($"Exports index ({index}) out of range ({exports.Count()}) for {File}:{defObjPath}");
        }
        return index;
    }

    public int CompareTo(UassetFile? other) {
        if (other == null) {
            return 1;
        }
        int mc = Mod.CompareTo(other.Mod);
        if (mc != 0) {
            return mc;
        }
        return File.CompareTo(other.File);
    }
    public bool Equals(UassetFile? other) => CompareTo(other) == 0;

    public UassetFile(SFModDataProvider Provider, string filename) {
        _provider = Provider;
        File = filename;
        Mod = SFModUtility.GetModName(filename);
    }
}

interface IComparableUasset {
    UassetFile UFile { get; set; }
    string DisplayName { get; set; }
    string? Tier { get; set; }

    int CompareTo(IComparableUasset? other) {
        if (other == null) {
            return 1;
        }
        return UFile.CompareTo(other.UFile);
    }
}

class UassetRecipe : IComparableUasset {
    public required UassetFile UFile { get; set; }
    public required string DisplayName { get; set; }
    public string? Tier { get; set; }
    public double? ManufacturingDuration { get; set; }
    public HashSet<UAssetMachine>? ProducedIn { get; set; }
    public HashSet<(int, UassetPart)> Ingredients = new HashSet<(int, UassetPart)>();
    public HashSet<(int, UassetPart)> Products = new HashSet<(int, UassetPart)>();
    public int? VariablePowerConstant;
    public int? VariablePowerFactor;

    public IEnumerable<GameDataRecipe> ToGameDataRecipe() {
        List<GameDataRecipe> result = new List<GameDataRecipe>();
        if (ProducedIn != null) {
            foreach (UAssetMachine prodMachine in ProducedIn) {
                GameDataRecipe mRecipe = new GameDataRecipe {
                    Name = DisplayName,
                    Tier = Tier ?? "0-0",
                    Machine = prodMachine.DisplayName,
                    BatchTime = SFModUtility.FractionStringFromDouble(ManufacturingDuration),
                    Parts = new List<GameDataRecipePart>(),
                    MinPower = VariablePowerFactor == null ? null : (-1 * (VariablePowerFactor + VariablePowerConstant ?? 0.0)).ToString(),
                    AveragePower = VariablePowerFactor == null ? null : (-1 * (VariablePowerFactor/2 + VariablePowerConstant ?? 0.0)).ToString(),
                    Alternate = !DisplayName.Contains("Alternate") ? null : true,
                };
                foreach ((int amount, UassetPart ing) in Ingredients) {
                    mRecipe.Parts = mRecipe.Parts.Append(ing.ToGameDataRecipePart(-1 *amount));
                }
                foreach ((int amount, UassetPart ing) in Products) {
                    mRecipe.Parts = mRecipe.Parts.Append(ing.ToGameDataRecipePart(amount));
                }

                result.Add(mRecipe);
            }
        }

        return result;
    }
}

class UAssetMachine : IComparableUasset {
    public required UassetFile UFile { get; set; }
    public required string DisplayName { get; set; }
    public string? Tier { get; set; }
    public int? PowerConsumption { get; set; }
    public double? PowerConsumptionExponent { get; set; }
    public int? ProductionShardSlotSize { get; set; }
    public double? ProductionShardBoostMultiplier { get; set; }
    public int? ProductionShardPowerExponent {
        get {
            if (ProductionShardBoostMultiplier != null) {
                return 2;
            }
            return null;
        }
    }
    public HashSet<(int, UassetPart)> Ingredients = new HashSet<(int, UassetPart)>();
    // Couldn't find where to get or calculate this, only used for geothermal generator anyways
    public int? MinPower {
        get {
            if (DisplayName == "Geothermal Generator" || DisplayName == "Build_GeneratorGeoThermal_C") {
                return 100;
            }
            return null;
        }
    }
    public int? BasePowerProduction { get; set; }
    public double? BaseBoostPercentage { get; set; }
    public double? BoostPercentage { get; set; }
    public SKBitmap[]? Icon { get; set; }

    public GameDataMachine ToGameDataMachine() {
        GameDataMachine result = new GameDataMachine {
            Name = DisplayName,
            Tier = Tier ?? "0-0",
            AveragePower = PowerConsumption == null ? null : (PowerConsumption * -1).ToString(),
            OverclockPowerExponent = SFModUtility.FractionStringFromDouble(PowerConsumptionExponent),
            MaxProductionShards = ProductionShardSlotSize,
            ProductionShardMultiplier = SFModUtility.FractionStringFromDouble(ProductionShardBoostMultiplier),
            ProductionShardPowerExponent = ProductionShardPowerExponent?.ToString(),
            Cost = Ingredients.Select(pair => pair.Item2.ToGameDataRecipePart(pair.Item1)),
            MinPower = MinPower?.ToString(),
            BasePower = BasePowerProduction?.ToString(),
            BasePowerBoost = SFModUtility.FractionStringFromDouble(BaseBoostPercentage),
            FueledBasePowerBoost = SFModUtility.FractionStringFromDouble(BoostPercentage + BaseBoostPercentage)
        };

        return result;
    }
}

class UassetPart : IComparableUasset {
    public required UassetFile UFile { get; set; }
    public required string DisplayName { get; set; }
    public string? Tier { get; set; }
    public int? SinkPoints { get; set; }
    public SKBitmap[]? Icon { get; set; }

    public GameDataItem ToGameDataItem() {
        GameDataItem result = new GameDataItem {
            Name = DisplayName,
            Tier = Tier ?? "0-0",
            SinkPoints = SinkPoints ?? 0,
        };

        return result;
    }

    public GameDataRecipePart ToGameDataRecipePart(int amount) {
        GameDataRecipePart result = new GameDataRecipePart {
            Part = DisplayName,
            Amount = amount.ToString(),
        };

        return result;
    }
}