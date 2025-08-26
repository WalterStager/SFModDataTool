
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
}