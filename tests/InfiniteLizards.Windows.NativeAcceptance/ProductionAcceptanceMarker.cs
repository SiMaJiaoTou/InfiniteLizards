using System.Security.Cryptography;

namespace InfiniteLizards.Windows.NativeAcceptance;

/// <summary>
/// Launch-scoped visual ownership marker. A prior keepalive pet cannot satisfy
/// a later launch's DWM oracle because body and pupil colors are nonce-derived.
/// </summary>
internal readonly record struct ProductionAcceptanceMarker(
    byte BodyRed,
    byte BodyGreen,
    byte BodyBlue,
    byte PupilRed,
    byte PupilGreen,
    byte PupilBlue)
{
    internal uint BodyDibRgb => DibRgb(BodyRed, BodyGreen, BodyBlue);
    internal uint PupilDibRgb => DibRgb(PupilRed, PupilGreen, PupilBlue);

    internal static ProductionAcceptanceMarker Derive(Guid nonce)
    {
        if (nonce == Guid.Empty)
        {
            throw new ArgumentException("A non-empty launch nonce is required.", nameof(nonce));
        }

        Span<byte> nonceBytes = stackalloc byte[16];
        _ = nonce.TryWriteBytes(nonceBytes);
        Span<byte> digest = stackalloc byte[32];
        _ = SHA256.HashData(nonceBytes, digest);

        var bodyChannels = new byte[]
        {
            checked((byte)(16 + digest[0] % 64)),
            checked((byte)(96 + digest[1] % 64)),
            checked((byte)(192 + digest[2] % 64))
        };
        var permutation = digest[3] % 6;
        var (redIndex, greenIndex, blueIndex) = permutation switch
        {
            0 => (0, 1, 2),
            1 => (0, 2, 1),
            2 => (1, 0, 2),
            3 => (1, 2, 0),
            4 => (2, 0, 1),
            _ => (2, 1, 0)
        };

        // Pupils stay deliberately dark against the fixed white eye anchor,
        // while retaining another 18 launch-derived bits of identity.
        return new ProductionAcceptanceMarker(
            bodyChannels[redIndex],
            bodyChannels[greenIndex],
            bodyChannels[blueIndex],
            checked((byte)(digest[4] % 64)),
            checked((byte)(digest[5] % 64)),
            checked((byte)(digest[6] % 64)));
    }

    private static uint DibRgb(byte red, byte green, byte blue) =>
        ((uint)red << 16) | ((uint)green << 8) | blue;
}
