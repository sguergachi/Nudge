namespace NudgeCommon.Utilities;

/// <summary>
/// Provides deterministic hashing for strings that's stable across platforms and .NET versions.
/// Critical for ML features - ensures same app name always produces same hash.
/// </summary>
public static class StableHash
{
    /// <summary>
    /// Compute FNV-1a hash - fast, deterministic, minimal collisions
    /// </summary>
    /// <param name="text">Text to hash</param>
    /// <returns>32-bit signed integer hash (suitable for ML features)</returns>
    public static int GetHash(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        // FNV-1a algorithm - simple, fast, good distribution
        const uint fnvPrime = 16777619;
        uint hash = 2166136261;  // FNV offset basis

        foreach (char c in text)
        {
            hash ^= c;
            hash *= fnvPrime;
        }

        // Convert to signed int32 for compatibility with Python/TensorFlow
        return unchecked((int)hash);
    }
}
