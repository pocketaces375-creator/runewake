namespace Runewake.Engine.State;

/// <summary>
/// A deterministic, seedable PRNG based on SplitMix64.
/// Every call to <see cref="Next"/> advances the internal state and returns
/// a deterministic value. Cloning produces an independent copy at the same
/// position — used for "what-if" simulation branching.
/// </summary>
public sealed class SeededRng
{
    private ulong _state;

    /// <summary>
    /// Creates a new RNG from the given seed.
    /// </summary>
    public SeededRng(ulong seed)
    {
        _state = seed;
    }

    private SeededRng(SeededRng other)
    {
        _state = other._state;
    }

    /// <summary>
    /// Returns a deep clone of this RNG at its current position.
    /// </summary>
    public SeededRng Clone() => new(this);

    /// <summary>
    /// Returns the next pseudo-random 64-bit unsigned integer.
    /// </summary>
    public ulong NextU64()
    {
        _state += 0x9E3779B97F4A7C15;
        ulong z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
        return z ^ (z >> 31);
    }

    /// <summary>
    /// Returns a pseudo-random integer in [0, maxExclusive).
    /// Behaviour is undefined if maxExclusive is 0.
    /// </summary>
    public int NextInt(int maxExclusive)
    {
        return (int)(NextU64() % (ulong)maxExclusive);
    }

    /// <summary>
    /// Returns a pseudo-random integer in [minInclusive, maxExclusive).
    /// </summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        return minInclusive + NextInt(maxExclusive - minInclusive);
    }

    /// <summary>
    /// Returns true with the given probability (0.0 to 1.0).
    /// </summary>
    public bool NextBool(double probability = 0.5)
    {
        return (NextU64() & 0x7FFFFFFFFFFFFFFF) < (ulong)(probability * (double)long.MaxValue);
    }
}
