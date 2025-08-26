using Fractions;
using Newtonsoft.Json;
using SFModDataMerger;

namespace SFModDataExtractor;

class DataEntry {
    public required string Key { get; set; }
    public string? SourceString { get; set; }
    public string? Context { get; set; }
    public string? VariableDescription { get; set; }
    public string? Type { get; set; }
    public string? Tag { get; set; }

    public override string ToString() {
        return $"DataEntry({SourceString},{SourceString},{Context},{VariableDescription},{Type},{Tag})";
    }
}

class SinkPointsEntry {
    public required string Key { get; set; }
    public int? Points { get; set; }
    public string? ObjectName { get; set; }
    public string? ObjectPath { get; set; }

    public override string ToString() {
        return $"SinkPointsEntry({Points},{ObjectName},{ObjectPath})";
    }
}

class Mod {
    public required string Name;
    public required string Path;
    public List<string> Files = new List<string>();
    public Dictionary<string, Machine> Machines = new Dictionary<string, Machine>();
    public Dictionary<string, Recipe> MachineRecipes = new Dictionary<string, Recipe>();
    public Dictionary<string, Item> Items = new Dictionary<string, Item>();
    public Dictionary<string, Recipe> Recipes = new Dictionary<string, Recipe>();

    public override string ToString() {
        return $"Mod({Name},Path={Path},Files={Files.Count},Machines={Machines.Count},Items={Items.Count},Recipes={Recipes.Count},MchRcp={MachineRecipes.Count})";
    }
}

class Machine {
    public required string File { get; set; }
    public required string DisplayName { get; set; }
    public string? BuildFile { get; set; }
    public SuperReference? Super { get; set; }
    public float? AveragePower { get; set; }
    public float? OverclockPowerExponent { get; set; }
    public int? MaxProductionShards { get; set; }
    public float? ProductionShardsMultiplier { get; set; }
    public Recipe? Recipe { get; set; }

    public override string ToString() {
        return JsonConvert.SerializeObject(this, Formatting.Indented);
    }

    public GameDataMachine ToGameData() {
        return new GameDataMachine{
            Name = DisplayName,
            Tier = "0-0",
            AveragePower = AveragePower != null ? Fraction.FromDouble((double)AveragePower).ToString() : null,
            OverclockPowerExponent = OverclockPowerExponent != null ? Fraction.FromDouble((double)OverclockPowerExponent).ToString() : null,
            MaxProductionShards = MaxProductionShards,
            ProductionShardMultiplier = ProductionShardsMultiplier != null ? Fraction.FromDouble((double)ProductionShardsMultiplier).ToString() : null,
            ProductionShardPowerExponent = "2",
            Cost = Recipe?.ResolvedItemReferences.Where(i => i.isProduct == false).Select(i => i.ToGameData()),
        };
    }
}

class Item {
    public required string File { get; set; }
    public required string DisplayName { get; set; }
    public SuperReference? Super { get; set; }
    // public 

    public GameDataItem ToGameData() {
        return new GameDataItem {
            Name = DisplayName,
            Tier = "0-0",
            SinkPoints = 2,
        };
    }
}

class RecipeItemReference {
    public int? Ammount { get; set; }
    public required bool isProduct { get; set; }
    public string? ObjectName { get; set; }
    public string? ObjectPath { get; set; }

    public override string ToString() {
        return JsonConvert.SerializeObject(this, Formatting.Indented);
    }
}

class ResolvedRecipeItemReference {
    public int? Ammount { get; set; }
    public required bool isProduct { get; set; }
    public string? Name { get; set; }

    public override string ToString() {
        return JsonConvert.SerializeObject(this, Formatting.Indented);
    }

    public GameDataRecipePart ToGameData(bool includeProducts = true) {
        return new GameDataRecipePart { Part = Name, Amount = (isProduct ? Ammount : Ammount * -1).ToString() };
    }
}

class RecipeMachineReference {
    public string? AssetPathName { get; set; }
    public string? SubPathString { get; set; }

    public override string ToString() {
        return JsonConvert.SerializeObject(this, Formatting.Indented);
    }
}

class SuperReference {
    public string? ObjectName { get; set; }
    public string? ObjectPath { get; set; }
}

class Recipe {
    public required string File { get; set; }
    public required string DisplayName { get; set; }
    public SuperReference? Super { get; set; }
    public float? Duration { get; set; }
    public HashSet<RecipeItemReference> ItemReferences = new HashSet<RecipeItemReference>();
    public HashSet<RecipeMachineReference> MachineReferences = new HashSet<RecipeMachineReference>();
    public HashSet<ResolvedRecipeItemReference> ResolvedItemReferences = new HashSet<ResolvedRecipeItemReference>();
    public HashSet<string> ResolvedMachineReferences = new HashSet<string>();

    public override string ToString() {
        return JsonConvert.SerializeObject(this, Formatting.Indented);
    }

    public IEnumerable<GameDataRecipe> ToGameData() {
        return ResolvedMachineReferences.Select(m => new GameDataRecipe{
            Name = DisplayName,
            Tier = "0-0",
            Machine = m,
            BatchTime = Duration.ToString(), // ?? -1.0,
            Parts = ResolvedItemReferences.Select(i => i.ToGameData())
        });
    }
}