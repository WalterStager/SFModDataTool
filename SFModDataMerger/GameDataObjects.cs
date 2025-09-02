
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace SFModDataMerger;

public class GameDataRecipePart : IEquatable<GameDataRecipePart> {
    public required string Part;
    public required string Amount;

    public bool Equals(GameDataRecipePart? other) => Part.Equals(other?.Part);
    public override int GetHashCode() => HashCode.Combine(Part);
}

public class GameDataMMMachine : IEquatable<GameDataRecipe> {
    public required string Name;
    public string? PartsRatio;
    public bool? Default;

    public bool Equals(GameDataRecipe? other) => Name.Equals(other?.Name);
    public override int GetHashCode() => HashCode.Combine(Name);
}

public class GameDataMMCapacity : IEquatable<GameDataMMCapacity> {
    public required string Name;
    public string? PartsRatio;
    public bool? Default;
    public int? Color;

    public bool Equals(GameDataMMCapacity? other) => Name.Equals(other?.Name);
    public override int GetHashCode() => HashCode.Combine(Name);
}

public class GameDataMachine : IEquatable<GameDataMachine> {
    public required string Name;
    public required string Tier;
    public string? AveragePower;
    public string? OverclockPowerExponent;
    public int? MaxProductionShards;
    public string? ProductionShardMultiplier;
    public string? ProductionShardPowerExponent;
    public IEnumerable<GameDataRecipePart>? Cost;
    public string? MinPower;
    public string? BasePower;
    public string? BasePowerBoost;
    public string? FueledBasePowerBoost;

    public bool Equals(GameDataMachine? other) => Name.Equals(other?.Name);
    public override int GetHashCode() => HashCode.Combine(Name);
}

public class GameDataMultiMachine : IEquatable<GameDataMultiMachine> {
    public required string Name;
    public bool? ShowPpm;
    public bool? AutoRound;
    public string? DefaultMax;
    public IEnumerable<GameDataMMMachine>? Machines;
    public IEnumerable<GameDataMMCapacity>? Capacities;

    public bool Equals(GameDataMultiMachine? other) => Name.Equals(other?.Name);
    public override int GetHashCode() => HashCode.Combine(Name);
}

public class GameDataItem : IEquatable<GameDataItem> {
    public required string Name;
    public required string Tier;
    public required int? SinkPoints;

    public bool Equals(GameDataItem? other) => Name.Equals(other?.Name);
    public override int GetHashCode() => HashCode.Combine(Name);
}

public class GameDataRecipe : IEquatable<GameDataRecipe> {
    public required string Name;
    public required string Tier;
    public string? Machine;
    public string? BatchTime;
    public IEnumerable<GameDataRecipePart>? Parts;
    public string? MinPower;
    public string? AveragePower;
    public bool? Alternate;
    public bool? Ficsmas;

    public bool Equals(GameDataRecipe? other) => Name.Equals(other?.Name);
    public override int GetHashCode() => HashCode.Combine(Name);
}

public class GameData {
    public required HashSet<GameDataMachine> Machines;
    public required HashSet<GameDataMultiMachine> MultiMachines;
    public required HashSet<GameDataItem> Parts;
    public required HashSet<GameDataRecipe> Recipes;

    public static GameData ReadGameData(string filename) {
        try {
            string text = File.ReadAllText(filename);
            GameData? gameData = JsonConvert.DeserializeObject<GameData>(text);

            if (gameData == null)
                throw new Exception("Failed to parse game_data.json: root object is null.");

            return gameData;
        }
        catch (Exception ex) {
            throw new Exception($"Error parsing {filename}: {ex.Message}", ex);
        }
    }

    public void WriteGameData(string filename, bool backup = true) {
        if (backup) {
            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string? directoryPortion = Path.GetDirectoryName(filename);
            if (directoryPortion == null) {
                throw new Exception("Output file does not exist at a valid path, could not make backup file");
            }
            string BackupFilePath = Path.Combine(
                directoryPortion,
                Path.GetFileNameWithoutExtension(filename) + timestamp + Path.GetExtension(filename)
            );
            File.Copy(filename, BackupFilePath);
        }
        File.WriteAllText(filename, JsonConvert.SerializeObject(this, Formatting.Indented, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
    }

    public GameData Union(GameData data) {
        return new GameData {
            Machines = Machines.Union(data.Machines).ToHashSet(),
            MultiMachines = MultiMachines.Union(data.MultiMachines).ToHashSet(),
            Parts = Parts.Union(data.Parts).ToHashSet(),
            Recipes = Recipes.Union(data.Recipes).ToHashSet(),
        };
    }
}