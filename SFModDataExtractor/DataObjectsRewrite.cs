using System.Linq.Expressions;
using System.Text.RegularExpressions;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using ZstdSharp.Unsafe;

namespace SFModDataExtractor;

enum UassetType {
    RecipeDesc,
    MachineDesc,
    MachineBuild,
    ItemDesc,
    Schematic,
    Texture,
    Other
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
                JToken? superPathToken = exports[0].SelectToken("Super.ObjectPath");
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
                string? objClass = GetString(0, "Class");
                if (objClass != null) {
                    switch (objClass) {
                        case "UScriptClass'Texture2D'":
                            _type = UassetType.Texture;
                            return (UassetType)_type;
                    }
                }
                string? superStruct = GetString(0, "SuperStruct.ObjectName");
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
                    _ => UassetType.Other,
                };
            }
            return (UassetType)_type;
        }
    }

    public UObject GetUObject(int objIndex) {
        return _provider.FileProvider.LoadPackage(File).GetExports().ToArray()[objIndex];
    }

    public JToken? GetToken(int exportIndex, string tokenPath, bool searchSuper = true) {
        if (exportIndex >= exports.Count()) {
            throw new Exception($"Exports index ({exportIndex}) out of range ({exports.Count()})");
        }
        JToken? result = exports[exportIndex].SelectToken(tokenPath);
        if (result != null && result.ToString() != "") {
            return result;
        }
        if (searchSuper) {
            result = super?.GetToken(exportIndex, tokenPath, searchSuper);
        }
        return result;
    }

    public string? GetString(int exportIndex, string tokenPath, bool searchSuper = true) {
        JToken? result = GetToken(exportIndex, tokenPath, searchSuper);
        if (result?.Type is JTokenType.String) {
            return result.ToString();
        }
        return null;
    }

    public int? GetInt(int exportIndex, string tokenPath, bool searchSuper = true) {
        string? result = GetString(exportIndex, tokenPath, searchSuper);
        if (result != null) {
            return int.Parse(result);
        }
        return null;
    }

    public double? GetDouble(int exportIndex, string tokenPath, bool searchSuper = true) {
        string? result = GetString(exportIndex, tokenPath, searchSuper);
        if (result != null) {
            return double.Parse(result);
        }
        return null;
    }

    public int GetDefaultObjectIndex() {
        string? defObjPath = GetString(0, "ClassDefaultObject.ObjectPath");
        if (defObjPath == null) {
            throw new Exception($"Couldn't get default object path for {File}");
        }
        return SFModUtility.GetAssetPathIndex(defObjPath);
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
    // power consumption fields
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
            if (DisplayName == "Geothermal Generator") {
                return 100;
            }
            return null;
        }
    }
    public int? BasePowerProduction { get; set; }
    public double? BaseBoostPercentage { get; set; }
    public double? BoostPercentage { get; set; }
    public SKBitmap[]? Icon { get; set; }
}

class UassetPart : IComparableUasset {
    public required UassetFile UFile { get; set; }
    public required string DisplayName { get; set; }
    public string? Tier { get; set; }
    public int? SinkPoints { get; set; }
    public SKBitmap[]? Icon { get; set; }
}