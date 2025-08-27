
// random static functions
class SFModUtility {
    public static int CountCommonPrefix<T>(IEnumerable<T> a, IEnumerable<T> b) {
        int count = 0;
        int minLength = Math.Min(a.Count(), b.Count());
        for (int i = 0; i < minLength; i++) {
            if (!a.Equals(b)) {
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
}