
// random static functions
using System.Text.RegularExpressions;

class SFModUtility {
    public static int CountCommonPrefix<T>(IEnumerable<T> a, IEnumerable<T> b) {
        int count = 0;
        foreach ((T ae, T be) in a.Zip(b)) {
            if (!ae.Equals(be)) {
                break;
            }
            count++;
        }
        return count;
    }

    public static int GetAssetPathIndex(string assetPath) {
        string? ext = Path.GetExtension(assetPath)?.Substring(1);
        if (ext == null) {
            throw new Exception($"Failed to get index from asset path {assetPath}");
        }
        return int.Parse(ext);
    }

    public static string GetModName(string filename) {
        if (filename.Contains("Mods")) {
            Match modNameMatch = Regex.Match(filename, @"/Mods/([^/]+)/", RegexOptions.IgnoreCase);
            string? modName = modNameMatch.Groups[1].Value;
            if (modName == null || modName == "") {
                throw new Exception($"Could not get mod from filename {filename}");
            }
            return modName;
        }
        else {
            return "FactoryGame";
        }
    }
}