
// random static functions
using System.Text.RegularExpressions;
using Fractions;
using SFModDataExtractor;

class SFModUtility {
    public static int CountCommonPrefix<T>(IEnumerable<T> a, IEnumerable<T> b) {
        int count = 0;
        foreach ((T ae, T be) in a.Zip(b)) {
            if (ae == null || !ae.Equals(be)) {
                break;
            }
            count++;
        }
        return count;
    }

    public static int GetAssetPathIndex(string assetPath) {
        string? ext = Path.GetExtension(assetPath)?.Substring(1);
        if (ext == null) {
            throw new SFModDataRuntimeException($"Failed to get index from asset path {assetPath}");
        }
        return int.Parse(ext);
    }

    public static string GetModName(string filename) {
        if (filename.Contains("Mods")) {
            Match modNameMatch = Regex.Match(filename, @"/Mods/([^/]+)/", RegexOptions.IgnoreCase);
            string? modName = modNameMatch.Groups[1].Value;
            if (modName == null || modName == "") {
                throw new SFModDataRuntimeException($"Could not get mod from filename {filename}");
            }
            return modName;
        }
        else {
            return "FactoryGame";
        }
    }

    public static string? FractionStringFromDouble(double? num) {
        if (num == null) {
            return null;
        }
        return Fraction.FromDouble((double)num).ToString();
    }

    public static string IncrementAltRecipeName(string inputName) {
        string pattern = @"\(Alt (\d+)\)$";

        if (Regex.IsMatch(inputName, pattern)) {
            return Regex.Replace(inputName, pattern, m => {
                int num = int.Parse(m.Groups[1].Value);
                return $"(Alt {num + 1})";
            });
        }
        else {
            return inputName + " (Alt 1)";
        }
    }
}