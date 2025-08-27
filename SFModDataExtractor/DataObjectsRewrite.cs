using System.Linq.Expressions;
using System.Text.RegularExpressions;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using Newtonsoft.Json.Linq;
using ZstdSharp.Unsafe;

namespace SFModDataExtractor;

enum UassetType {
    RecipeDesc,
    MachineDesc,
    MachineBuild,
    ItemDesc,
    Schematic,
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
                string? superStruct = GetString(0, "SuperStruct.ObjectName");
                _type = superStruct switch {
                    "Class'FGRecipe'" => UassetType.RecipeDesc,
                    "Class'FGBuildableManufacturer'" => UassetType.MachineBuild,
                    "Class'FGBuildingDescriptor'" => UassetType.MachineDesc,
                    "Class'FGItemDescriptor'" => UassetType.ItemDesc,
                    "Class'FGSchematic'" => UassetType.Schematic,
                    _ => UassetType.Other,
                };
            }
            return (UassetType)_type;
        }
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
        Mod = Provider.GetModName(filename);
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
    public float? ManufacturingDuration { get; set; }
    public IEnumerable<UAssetMachine>? ProducedIn { get; set; }
    public IEnumerable<UassetPart>? Ingredients { get; set; }
    public IEnumerable<UassetPart>? Products { get; set; }
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
    public IEnumerable<UassetPart>? Ingredients { get; set; }
    // Couldn't find where to get or calculate this, only used for generator anyways
    public int? MinPower {
        get {
            if (DisplayName == "Geothermal Generator") {
                return 100;
            }
            return null;
        }
        set {
        }
    }
}

class UassetPart : IComparableUasset {
    public required UassetFile UFile { get; set; }
    public required string DisplayName { get; set; }
    public string? Tier { get; set; }
    public int? SinkPoints { get; set; }
}