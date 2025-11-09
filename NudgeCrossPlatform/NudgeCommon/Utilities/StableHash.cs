using System.Security.Cryptography;
using System.Text;

namespace NudgeCommon.Utilities;

/// <summary>
/// Provides deterministic hashing for strings that's stable across platforms and .NET versions.
/// Critical for ML features - ensures same app name always produces same hash.
/// </summary>
public static class StableHash
{
    /// <summary>
    /// Compute FNV-1a hash - fast, deterministic, no collisions for reasonable dataset sizes
    /// </summary>
    /// <param name="text">Text to hash</param>
    /// <returns>32-bit signed integer hash (suitable for ML features)</returns>
    public static int GetDeterministicHashCode(string text)
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

    /// <summary>
    /// Alternative: Use SHA256 and take first 4 bytes as int32
    /// More robust but slower - use if FNV-1a has issues
    /// </summary>
    public static int GetCryptographicHashCode(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(text));

        // Take first 4 bytes and convert to int32
        return BitConverter.ToInt32(hash, 0);
    }

    /// <summary>
    /// Get hash with collision tracking for debugging
    /// </summary>
    public static (int hash, bool isPotentialCollision) GetHashWithCollisionCheck(
        string text,
        Dictionary<int, string> seenHashes)
    {
        int hash = GetDeterministicHashCode(text);
        bool isCollision = false;

        if (seenHashes.TryGetValue(hash, out string? existing))
        {
            if (existing != text)
            {
                isCollision = true;
                Console.WriteLine($"WARNING: Hash collision detected!");
                Console.WriteLine($"  '{existing}' and '{text}' both hash to {hash}");
            }
        }
        else
        {
            seenHashes[hash] = text;
        }

        return (hash, isCollision);
    }
}
