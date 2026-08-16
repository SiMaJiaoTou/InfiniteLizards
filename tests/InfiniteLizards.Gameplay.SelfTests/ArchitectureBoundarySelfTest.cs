using DesktopPet.Engine;
using InfiniteLizards.Gameplay;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

/// <summary>
/// Guards the physical dependency direction promised by the architecture:
/// Engine is generic infrastructure; gameplay may depend on Engine; neither
/// assembly may depend on the Avalonia/native desktop host.
/// </summary>
internal static class ArchitectureBoundarySelfTest
{
    private static readonly string[] ForbiddenDesktopReferences =
    {
        "Avalonia",
        "InfiniteLizards.Desktop",
        "PresentationCore",
        "PresentationFramework",
        "System.Windows",
        "WindowsBase"
    };

    public static ArchitectureBoundaryResult Run()
    {
        try
        {
            var engine = typeof(IDesktopPetGame<>).Assembly;
            var gameplay = typeof(LizardGameModule).Assembly;
            var desktop = Assembly.Load("InfiniteLizards.Desktop");
            var engineReferences = ReferencedAssemblyNames(engine);
            var gameplayReferences = ReferencedAssemblyNames(gameplay);

            AssertDoesNotReference(engine.GetName().Name!, engineReferences, ForbiddenDesktopReferences);
            AssertDoesNotReference(
                engine.GetName().Name!,
                engineReferences,
                ["InfiniteLizards.Gameplay"]);
            AssertDoesNotReference(gameplay.GetName().Name!, gameplayReferences, ForbiddenDesktopReferences);

            if (!gameplayReferences.Contains("DesktopPet.Engine"))
            {
                throw new InvalidOperationException(
                    "InfiniteLizards.Gameplay must depend on DesktopPet.Engine through the public game contract.");
            }

            var leakedDomainType = engine.ExportedTypes.FirstOrDefault(type =>
                type.Namespace?.StartsWith("InfiniteLizards", StringComparison.Ordinal) == true ||
                type.Namespace?.StartsWith("DesktopLizard", StringComparison.Ordinal) == true);
            if (leakedDomainType is not null)
            {
                throw new InvalidOperationException(
                    $"DesktopPet.Engine public surface leaks domain type {leakedDomainType.FullName}.");
            }

            AssertPhysicalGameplaySourceOwnership(gameplay);
            AssertGameplayCannotOwnDisplayCadence(gameplay);
            AssertGenericDesktopHostBoundary(desktop, gameplay);

            return new ArchitectureBoundaryResult(
                true,
                "Engine has no gameplay/UI/native dependency; gameplay physically owns its domain sources without an external shared-source glob, exposes no display-frame adapter, accumulator, DPI/frame-cadence DTO, or view-space debug telemetry, and references only the generic fixed-step Engine boundary; legacy WPF diagnostics are compiled only by their host/self-tests; PetWindow<TSnapshot> owns no raw game port and depends on DesktopPetRuntime<TSnapshot> plus the desktop presenter port.");
        }
        catch (Exception exception)
        {
            return new ArchitectureBoundaryResult(false, exception.Message);
        }
    }

    private static void AssertGameplayCannotOwnDisplayCadence(Assembly gameplay)
    {
        foreach (var legacyTypeName in new[]
                 {
                     "DesktopLizard.AppRuntime.PetSimulationFrameInput",
                     "DesktopLizard.AppRuntime.PetSimulationFrameOutput",
                     "DesktopLizard.AppRuntime.LegacyPetSimulationFrameAdapter",
                     "DesktopLizard.AppRuntime.DebugTelemetryFrameInput",
                     "DesktopLizard.AppRuntime.DebugTelemetryCoordinator",
                     "DesktopLizard.AppRuntime.LegacyDebugTelemetryFrameInput",
                     "DesktopLizard.AppRuntime.LegacyDebugTelemetryCoordinator",
                     "DesktopLizard.Core.DebugFrameSnapshot"
                 })
        {
            if (gameplay.GetType(legacyTypeName, throwOnError: false) is not null)
            {
                throw new InvalidOperationException(
                    $"Gameplay assembly must not contain compatibility display-frame type {legacyTypeName}.");
            }
        }

        var session = gameplay.GetType(
            "DesktopLizard.AppRuntime.PetSimulationSession",
            throwOnError: true)!;
        if (session.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Any(field => field.FieldType == typeof(FixedStepRunner)))
        {
            throw new InvalidOperationException(
                "Gameplay session must not retain its own display-frame accumulator.");
        }
        if (session.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                               BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Any(method => method.Name == "Advance"))
        {
            throw new InvalidOperationException(
                "Gameplay session may expose fixed-step advance only; raw display-frame Advance belongs to a host adapter.");
        }
        foreach (var forbiddenMethod in new[]
                 {
                     "CaptureDebugSnapshot",
                     "ClearDebugPath"
                 })
        {
            if (session.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Any(method => method.Name == forbiddenMethod))
            {
                throw new InvalidOperationException(
                    $"Gameplay session must not own legacy view telemetry method {forbiddenMethod}.");
            }
        }

        var forbiddenDisplayMemberNames = new HashSet<string>(
            ["FrameDelta", "DpiScale", "ModelToViewScale"],
            StringComparer.OrdinalIgnoreCase);
        foreach (var type in gameplay.GetTypes())
        {
            if (type.Name.Contains("DebugTelemetry", StringComparison.Ordinal) ||
                type.Name.Equals("DebugFrameSnapshot", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Gameplay assembly must not own view-space diagnostic type {type.FullName}.");
            }

            var displayMember = type
                .GetMembers(BindingFlags.Instance | BindingFlags.Static |
                            BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.DeclaredOnly)
                .FirstOrDefault(member =>
                    forbiddenDisplayMemberNames.Contains(member.Name));
            if (displayMember is not null)
            {
                throw new InvalidOperationException(
                    $"Gameplay assembly must not own display member {type.FullName}.{displayMember.Name}.");
            }

            var displayParameter = type
                .GetMethods(BindingFlags.Instance | BindingFlags.Static |
                            BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.DeclaredOnly)
                .SelectMany(method => method.GetParameters())
                .FirstOrDefault(parameter =>
                    parameter.Name is { } name &&
                    forbiddenDisplayMemberNames.Contains(name));
            if (displayParameter is not null)
            {
                throw new InvalidOperationException(
                    $"Gameplay assembly must not accept display parameter {type.FullName}.{displayParameter.Member.Name}({displayParameter.Name}).");
            }
        }
    }

    private static void AssertPhysicalGameplaySourceOwnership(Assembly gameplay)
    {
        var repositoryRoot = FindRepositoryRoot();
        var gameplayDirectory = Path.Combine(
            repositoryRoot,
            "src",
            "InfiniteLizards.Gameplay");
        var projectPath = Path.Combine(
            gameplayDirectory,
            "InfiniteLizards.Gameplay.csproj");
        var project = XDocument.Load(projectPath);
        var externalCompileItem = project
            .Descendants()
            .Where(element => element.Name.LocalName == "Compile")
            .Select(element => (string?)element.Attribute("Include"))
            .FirstOrDefault(include =>
                include?.Split('/', '\\').Contains("..", StringComparer.Ordinal) == true);
        if (externalCompileItem is not null)
        {
            throw new InvalidOperationException(
                $"Gameplay source must be physically owned by its project; external Compile item '{externalCompileItem}' is forbidden.");
        }

        foreach (var relativePath in new[]
                 {
                     Path.Combine("Core", "ProceduralLizard.cs"),
                     Path.Combine("Application", "PetSimulationSession.cs")
                 })
        {
            if (!File.Exists(Path.Combine(gameplayDirectory, relativePath)))
            {
                throw new InvalidOperationException(
                    $"Gameplay-owned source is missing from its project tree: {relativePath}.");
            }
        }

        foreach (var forbiddenGameplayPath in new[]
                 {
                     Path.Combine("Application", "DebugTelemetryCoordinator.cs"),
                     Path.Combine("Core", "DebugFrameSnapshot.cs")
                 })
        {
            if (File.Exists(Path.Combine(gameplayDirectory, forbiddenGameplayPath)))
            {
                throw new InvalidOperationException(
                    $"Display-oriented legacy diagnostics must not be physically owned by Gameplay: {forbiddenGameplayPath}.");
            }
        }

        var legacyCoreDirectory = Path.Combine(repositoryRoot, "Core");
        if (Directory.Exists(legacyCoreDirectory) &&
            Directory.EnumerateFiles(
                    legacyCoreDirectory,
                    "*.cs",
                    SearchOption.AllDirectories)
                .Any())
        {
            throw new InvalidOperationException(
                "The legacy WPF project must not compile a second copy of gameplay sources from the root Core directory.");
        }

        foreach (var legacyPath in new[]
                 {
                     Path.Combine(repositoryRoot, "Application", "PetSimulationSession.cs"),
                     Path.Combine(repositoryRoot, "Application", "DebugTelemetryCoordinator.cs")
                 })
        {
            if (File.Exists(legacyPath))
            {
                throw new InvalidOperationException(
                    $"The legacy WPF project must not compile a second gameplay source copy: {legacyPath}.");
            }
        }

        var legacyDebugFiles = new[]
        {
            Path.Combine("Application", "LegacyDebugFrameSnapshot.cs"),
            Path.Combine("Application", "LegacyDebugTelemetryCoordinator.cs"),
            Path.Combine("Application", "LegacyPetSimulationFrameAdapter.cs")
        };
        foreach (var legacyDebugFile in legacyDebugFiles)
        {
            if (!File.Exists(Path.Combine(repositoryRoot, legacyDebugFile)))
            {
                throw new InvalidOperationException(
                    $"The compatibility WPF diagnostic source is missing: {legacyDebugFile}.");
            }
        }

        var selfTestProject = XDocument.Load(Path.Combine(
            repositoryRoot,
            "tests",
            "InfiniteLizards.Gameplay.SelfTests",
            "InfiniteLizards.Gameplay.SelfTests.csproj"));
        var linkedCompileItems = selfTestProject
            .Descendants()
            .Where(element => element.Name.LocalName == "Compile")
            .Select(element => ((string?)element.Attribute("Include"))?
                .Replace('\\', '/'))
            .Where(include => include is not null)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var legacyDebugFile in legacyDebugFiles)
        {
            var expectedInclude = "../../" + legacyDebugFile.Replace('\\', '/');
            if (!linkedCompileItems.Contains(expectedInclude))
            {
                throw new InvalidOperationException(
                    $"Gameplay self-tests must explicitly compile compatibility diagnostic source {expectedInclude}.");
            }
        }

        var friendAssemblies = gameplay
            .GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(attribute => new AssemblyName(attribute.AssemblyName).Name)
            .ToHashSet(StringComparer.Ordinal);
        if (!friendAssemblies.Contains("DesktopLizard"))
        {
            throw new InvalidOperationException(
                "The compatibility WPF host must consume the single Gameplay assembly through its explicit friend boundary.");
        }

        var wpfProject = XDocument.Load(Path.Combine(repositoryRoot, "DesktopLizard.csproj"));
        var referencesGameplay = wpfProject
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Any(include => include is not null &&
                include.Replace('\\', '/').EndsWith(
                    "src/InfiniteLizards.Gameplay/InfiniteLizards.Gameplay.csproj",
                    StringComparison.Ordinal));
        if (!referencesGameplay)
        {
            throw new InvalidOperationException(
                "The legacy WPF host must reference Gameplay instead of compiling its domain sources.");
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "InfiniteLizards.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate the InfiniteLizards repository root from the self-test output directory.");
    }

    private static HashSet<string> ReferencedAssemblyNames(System.Reflection.Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static void AssertDoesNotReference(
        string owner,
        IReadOnlySet<string> actualReferences,
        IEnumerable<string> forbiddenPrefixes)
    {
        foreach (var prefix in forbiddenPrefixes)
        {
            var match = actualReferences.FirstOrDefault(reference =>
                reference.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                reference.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                throw new InvalidOperationException(
                    $"{owner} must not reference platform/domain assembly {match}.");
            }
        }
    }

    private static void AssertGenericDesktopHostBoundary(
        Assembly desktop,
        Assembly gameplay)
    {
        var petWindow = desktop.GetType(
            "InfiniteLizards.Desktop.PetWindow`1",
            throwOnError: true)!;
        if (!petWindow.IsGenericTypeDefinition || petWindow.GetGenericArguments().Length != 1)
        {
            throw new InvalidOperationException(
                "The desktop host must be the generic PetWindow<TSnapshot> type.");
        }

        var constructor = petWindow.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .SingleOrDefault();
        if (constructor is null)
        {
            throw new InvalidOperationException(
                "PetWindow<TSnapshot> must expose exactly one composition constructor.");
        }

        var parameters = constructor.GetParameters();
        if (parameters.Length < 2 ||
            !IsClosedOverSnapshot(parameters[0].ParameterType, typeof(DesktopPetRuntime<>), petWindow) ||
            parameters[1].ParameterType.GetGenericTypeDefinition().FullName !=
            "InfiniteLizards.Desktop.IDesktopPetPresenter`1" ||
            parameters[1].ParameterType.GetGenericArguments()[0] != petWindow.GetGenericArguments()[0])
        {
            throw new InvalidOperationException(
                "PetWindow<TSnapshot> must receive the Engine-owned DesktopPetRuntime<TSnapshot> and IDesktopPetPresenter<TSnapshot> from the composition root.");
        }

        if (petWindow.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Any(field => field.FieldType.IsGenericType &&
                          field.FieldType.GetGenericTypeDefinition() == typeof(IDesktopPetGame<>)))
        {
            throw new InvalidOperationException(
                "PetWindow<TSnapshot> must not retain a raw game port that can bypass the Engine scheduler.");
        }

        var signatureTypes = petWindow
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                       BindingFlags.DeclaredOnly)
            .Select(field => ($"field {field.Name}", field.FieldType))
            .Concat(petWindow
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                               BindingFlags.DeclaredOnly)
                .Select(property => ($"property {property.Name}", property.PropertyType)))
            .Concat(petWindow
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.DeclaredOnly)
                .SelectMany(method =>
                    method.GetParameters()
                        .Select(parameter => ($"method {method.Name}", parameter.ParameterType))
                        .Append(($"method {method.Name} return", method.ReturnType))))
            .Concat(parameters.Select(parameter =>
                ($"constructor parameter {parameter.Name}", parameter.ParameterType)));

        foreach (var (member, type) in signatureTypes)
        {
            var leaked = FindGameplayOrLizardType(type, gameplay);
            if (leaked is not null)
            {
                throw new InvalidOperationException(
                    $"PetWindow<TSnapshot> {member} leaks gameplay-specific type {leaked.FullName}.");
            }
        }
    }

    private static bool IsClosedOverSnapshot(
        Type candidate,
        Type genericDefinition,
        Type owner) =>
        candidate.IsGenericType &&
        candidate.GetGenericTypeDefinition() == genericDefinition &&
        candidate.GetGenericArguments()[0] == owner.GetGenericArguments()[0];

    private static Type? FindGameplayOrLizardType(Type type, Assembly gameplay)
    {
        if (type.HasElementType)
        {
            return FindGameplayOrLizardType(type.GetElementType()!, gameplay);
        }
        if (type.IsGenericParameter)
        {
            return null;
        }

        var typeNamespace = type.Namespace ?? string.Empty;
        if (type.Assembly == gameplay ||
            typeNamespace.StartsWith("DesktopLizard", StringComparison.Ordinal) ||
            typeNamespace.StartsWith("InfiniteLizards.Gameplay", StringComparison.Ordinal) ||
            type.Name is "LizardProfile" or "LizardGameModule" or "LizardView")
        {
            return type;
        }

        foreach (var argument in type.GetGenericArguments())
        {
            var leaked = FindGameplayOrLizardType(argument, gameplay);
            if (leaked is not null)
            {
                return leaked;
            }
        }
        return null;
    }
}

internal readonly record struct ArchitectureBoundaryResult(bool Passed, string Detail)
{
    public override string ToString() => Detail;
}
