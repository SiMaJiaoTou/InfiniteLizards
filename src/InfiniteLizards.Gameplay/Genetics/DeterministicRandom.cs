namespace InfiniteLizards.Gameplay.Genetics;

/// <summary>
/// Serializable state for the breeding random stream. The increment is part
/// of the state so a restored simulation continues bit-for-bit from the same
/// point instead of merely restarting from its original seed.
/// </summary>
public readonly record struct DeterministicRandomState(
    ulong State,
    ulong Increment);

/// <summary>
/// Small PCG32 stream owned by the breeding domain. It deliberately avoids
/// <see cref="Random"/> so framework upgrades cannot silently change saved
/// bloodlines, offspring, market stock, or generated names.
/// </summary>
internal sealed class DeterministicRandom
{
    private ulong _state;
    private readonly ulong _increment;

    public DeterministicRandom(ulong seed, ulong stream = 0x4C495A415244UL)
    {
        _state = 0UL;
        _increment = unchecked((stream << 1) | 1UL);
        _ = NextUInt32();
        _state = unchecked(_state + seed);
        _ = NextUInt32();
    }

    public DeterministicRandom(DeterministicRandomState state)
    {
        if ((state.Increment & 1UL) == 0UL)
        {
            throw new ArgumentException(
                "A deterministic random stream increment must be odd.",
                nameof(state));
        }

        _state = state.State;
        _increment = state.Increment;
    }

    public DeterministicRandomState CaptureState() => new(_state, _increment);

    public uint NextUInt32()
    {
        var previous = _state;
        _state = unchecked(previous * 6364136223846793005UL + _increment);
        var xorShifted = (uint)(((previous >> 18) ^ previous) >> 27);
        var rotation = (int)(previous >> 59);
        return (xorShifted >> rotation) |
               (xorShifted << ((-rotation) & 31));
    }

    public ulong NextUInt64() =>
        ((ulong)NextUInt32() << 32) | NextUInt32();

    public bool NextBoolean() => (NextUInt32() & 1U) != 0U;

    public bool NextChance(double probability)
    {
        if (!double.IsFinite(probability))
        {
            throw new ArgumentOutOfRangeException(nameof(probability));
        }

        return probability >= 1d ||
               probability > 0d && NextUnitDouble() < probability;
    }

    public int NextInt(int exclusiveMaximum)
    {
        if (exclusiveMaximum <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
        }

        // Rejection sampling removes modulo bias without changing the stream
        // contract for any accepted value.
        var bound = (uint)exclusiveMaximum;
        var threshold = unchecked((uint)(0U - bound)) % bound;
        uint value;
        do
        {
            value = NextUInt32();
        }
        while (value < threshold);

        return (int)(value % bound);
    }

    public double NextUnitDouble()
    {
        // Exactly 53 random bits, yielding [0,1) on every .NET platform.
        var high = (ulong)(NextUInt32() >> 5);
        var low = (ulong)(NextUInt32() >> 6);
        return (high * 67108864UL + low) / 9007199254740992d;
    }

    public double NextSymmetricTriangular()
    {
        // Bounded [-1,1] mutation noise. A bounded distribution makes even
        // deliberately extreme mutation settings finite and snapshot-safe.
        return NextUnitDouble() - NextUnitDouble();
    }
}
