using System.Text.Json;

namespace InfiniteLizards.Windows.NativeAcceptance;

internal static class ProductionAcceptancePureSelfTests
{
    internal static void Run()
    {
        AbsoluteNormalizationCoversNegativeVirtualDesktop();
        SendInputPrefixClassificationIsFailSafe();
        StableControlProtocolDoesNotAllocatePerRunMessages();
        AcceptanceConfigurationFreezesTheDefaultPalette();
        CompositeOracleRequiresKnownBackgroundPaletteAndCoverage();
        Console.WriteLine("5/5 Windows production-acceptance pure self-tests passed.");
    }

    private static void AbsoluteNormalizationCoversNegativeVirtualDesktop()
    {
        var first = SafeInjectedMouse.NormalizeVirtualDesktopPoint(
            new Win32Native.Point(-1920, -1080),
            -1920,
            -1080,
            5760,
            3240);
        Equal((0, 0), first, "first virtual pixel");
        var last = SafeInjectedMouse.NormalizeVirtualDesktopPoint(
            new Win32Native.Point(3839, 2159),
            -1920,
            -1080,
            5760,
            3240);
        Equal((65_535, 65_535), last, "last virtual pixel");
        var sample = SafeInjectedMouse.NormalizeVirtualDesktopPoint(
            new Win32Native.Point(0, 0),
            -1920,
            -1080,
            5760,
            3240);
        Equal(
            checked((int)((1920L * 65_535L + 5759L / 2L) / 5759L)),
            sample.X,
            "negative-origin normalized X");
    }

    private static void SendInputPrefixClassificationIsFailSafe()
    {
        Require(!SafeInjectedMouse.MayHaveAcceptedLeftDown(0),
            "0 events cannot include DOWN");
        Require(!SafeInjectedMouse.MayHaveAcceptedLeftDown(1),
            "the MOVE-only prefix cannot include DOWN");
        Require(SafeInjectedMouse.MayHaveAcceptedLeftDown(2),
            "MOVE+DOWN must enter cleanup state");
        Require(SafeInjectedMouse.MayHaveAcceptedLeftDown(3),
            "a complete batch remains pending until acknowledgement");
    }

    private static void StableControlProtocolDoesNotAllocatePerRunMessages()
    {
        Equal(
            "InfiniteLizards.Windows.NativeAcceptance.v1.Control",
            ProductionAcceptanceProtocol.StableMessageName,
            "stable registered-message name");
        var firstNonce = Guid.ParseExact(
            "00112233445566778899aabbccddeeff",
            "N");
        var secondNonce = Guid.ParseExact(
            "10112233445566778899aabbccddeeff",
            "N");
        var first = ProductionAcceptanceProtocol.DeriveControlToken(firstNonce);
        Equal(first, ProductionAcceptanceProtocol.DeriveControlToken(firstNonce),
            "deterministic control token");
        Require(first != 0 && first !=
                ProductionAcceptanceProtocol.DeriveControlToken(secondNonce),
            "nonce tokens must be nonzero and launch-specific");
        Equal(
            unchecked((long)(((ulong)7 << 32) | 3u)),
            ProductionAcceptanceProtocol.PackPayload(3, 7),
            "command/counter payload");
        Equal(7, ProductionAcceptanceProtocol.CounterSafetyReleaseUp,
            "production safety counter ID");
        Equal(
            unchecked((nuint)0x494C5A4453414645UL),
            Win32Native.SafetyReleaseExtraInfo,
            "shared x64 safety tag");
    }

    private static void AcceptanceConfigurationFreezesTheDefaultPalette()
    {
        var marker = ProductionAcceptanceMarker.Derive(Guid.ParseExact(
            "00112233445566778899aabbccddeeff",
            "N"));
        using var document = JsonDocument.Parse(
            ProductionDesktopProcess.CreateAcceptanceConfigurationJson(marker));
        var root = document.RootElement;
        Equal(6, root.GetProperty("SchemaVersion").GetInt32(), "schema");
        Equal(41821, root.GetProperty("IndividualSeed").GetInt32(), "seed");
        Equal(60d,
            root.GetProperty("Behavior").GetProperty("Timing")
                .GetProperty("InitialSpawnDuration").GetDouble(),
            "frozen spawn duration");
        Require(!root.GetProperty("IndividualVariation")
                .GetProperty("Enabled").GetBoolean(),
            "individual color variation must be disabled");
        var appearance = root.GetProperty("Appearance");
        Equal((int)marker.BodyRed,
            appearance.GetProperty("BodyColor").GetProperty("Red").GetInt32(),
            "launch-marker body red");
        Equal((int)marker.PupilBlue,
            appearance.GetProperty("PupilColor").GetProperty("Blue").GetInt32(),
            "launch-marker pupil blue");
        var secondary = root.GetProperty("SecondaryMotion");
        Equal(0d, secondary.GetProperty("BreathingAmplitude").GetDouble(),
            "breathing freeze");
        Equal(0d, secondary.GetProperty("IdleTailAmplitude").GetDouble(),
            "tail freeze");
        Equal(60d, secondary.GetProperty("BlinkInitialDelay").GetDouble(),
            "blink freeze");
    }

    private static void CompositeOracleRequiresKnownBackgroundPaletteAndCoverage()
    {
        const int width = 100;
        const int height = 100;
        var expectedBackground = ProductionCompositeCapture.ColorRefToDibRgb(
            NativeWindowChild.ProductionProbeColor);
        Equal(0x001F2F3Fu, expectedBackground, "COLORREF-to-DIB probe color");
        var pixels = Enumerable.Repeat(unchecked((int)expectedBackground), width * height)
            .ToArray();
        static bool Region(int x, int y) => x is >= 20 and < 80 && y is >= 10 and < 90;
        var marker = ProductionAcceptanceMarker.Derive(Guid.ParseExact(
            "00112233445566778899aabbccddeeff",
            "N"));
        var nextMarker = ProductionAcceptanceMarker.Derive(Guid.ParseExact(
            "10112233445566778899aabbccddeeff",
            "N"));
        Require(marker != nextMarker,
            "different nonces must derive different visual ownership markers");
        var bodyToProbeDistance = Math.Abs(marker.BodyRed - 31) +
            Math.Abs(marker.BodyGreen - 47) + Math.Abs(marker.BodyBlue - 63);
        Require(bodyToProbeDistance >= 160,
            "the launch body marker must be high-contrast against the probe background");
        Require(marker.PupilRed < 64 && marker.PupilGreen < 64 && marker.PupilBlue < 64,
            "the launch pupil marker must remain a dark anchor inside the white eye");

        PaintRegionPixels(pixels, width, Region, 0, 400, marker.BodyDibRgb);
        PaintRegionPixels(pixels, width, Region, 400, 60, 0x00FFFFFFu);
        PaintRegionPixels(pixels, width, Region, 460, 80, marker.PupilDibRgb);
        var valid = ProductionCompositeCapture.AnalyzePixels(
            width,
            height,
            pixels,
            Region,
            marker);
        Require(valid.IsLaunchMarkedLizardFrame(out var validReason),
            $"known default palette should pass: {validReason}");

        var blob = Enumerable.Repeat(unchecked((int)expectedBackground), width * height)
            .ToArray();
        PaintRegionPixels(blob, width, Region, 0, 600, 0x00000000u);
        var blobObservation = ProductionCompositeCapture.AnalyzePixels(
            width,
            height,
            blob,
            Region,
            marker);
        Require(!blobObservation.IsLaunchMarkedLizardFrame(out _),
            "an arbitrary nontrivial black blob must not impersonate the lizard");

        pixels[0] = unchecked((int)0x00AA00AAu);
        var contaminated = ProductionCompositeCapture.AnalyzePixels(
            width,
            height,
            pixels,
            Region,
            marker);
        Require(contaminated.ExpectedBackgroundExteriorPixels ==
                contaminated.ExteriorPixels - 1,
            "the oracle must compare every exterior pixel with the known probe color");

        var staleMarker = ProductionCompositeCapture.AnalyzePixels(
            width,
            height,
            pixels,
            Region,
            nextMarker);
        Require(!staleMarker.IsLaunchMarkedLizardFrame(out _),
            "a prior launch's marker pixels must not prove ownership of the new HWND");
    }

    private static void PaintRegionPixels(
        int[] pixels,
        int width,
        Func<int, int, bool> region,
        int skip,
        int count,
        uint rgb)
    {
        var seen = 0;
        var painted = 0;
        for (var index = 0; index < pixels.Length && painted < count; index++)
        {
            var x = index % width;
            var y = index / width;
            if (!region(x, y) || seen++ < skip)
            {
                continue;
            }
            pixels[index] = unchecked((int)rgb);
            painted++;
        }
        Equal(count, painted, "painted synthetic region pixels");
    }

    private static void Equal<T>(T expected, T actual, string name)
        where T : IEquatable<T>
    {
        if (!expected.Equals(actual))
        {
            throw new InvalidOperationException(
                $"{name}: expected {expected}, actual {actual}.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
