using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DraftEngine.Updates;

namespace DraftEngine.Tools.UpdateKeyTool;

/// <summary>
/// Offline signing utility for DraftEngine update manifests.
/// <para>keygen — creates an ECDSA P-256 key pair outside the repository and prints the public key.</para>
/// <para>sign — signs a manifest with the private key (which must live outside the repository).</para>
/// <para>verify — checks a signed manifest against a public key or the configured app key.</para>
/// The private key never needs to leave the maintainer's machine and is never read by the app.
/// </summary>
public static class Program
{
    private const string Usage = """
        UpdateKeyTool — DraftEngine update manifest signing

          UpdateKeyTool keygen --out <path-outside-repo/update-signing-key.pem>
              Creates an ECDSA P-256 key pair. The private key file must be outside the repository.
              Prints the public key (SPKI base64) to paste into appsettings.json Updates:PublicKey.

          UpdateKeyTool sign --manifest <manifest.json> --key <private-key.pem> --out <signed.json>
              Signs the canonical manifest; refuses private keys inside the repository.

          UpdateKeyTool verify --manifest <signed.json> --public-key <base64-or-file>
              Parses and verifies the manifest signature.

        Exit codes: 0 success, 1 invalid input, 2 refused (repository path), 3 verification failed.
        """;

    /// <summary>Entry point.</summary>
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "keygen" => Keygen(ParseOptions(args[1..])),
                "sign" => Sign(ParseOptions(args[1..])),
                "verify" => Verify(ParseOptions(args[1..])),
                _ => Fail(Usage),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or FormatException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Keygen(IReadOnlyDictionary<string, string> options)
    {
        if (!options.TryGetValue("out", out var output) || string.IsNullOrWhiteSpace(output))
        {
            return Fail("keygen requires --out <path>.");
        }

        var fullPath = Path.GetFullPath(output);
        if (RepositoryGuard.IsInsideRepository(fullPath))
        {
            return Refused($"refusing to write a private key inside the repository: {fullPath}\n" +
                           "Choose a path outside the repository (e.g. your user profile or a backed-up folder).");
        }

        if (File.Exists(fullPath))
        {
            return Fail($"refusing to overwrite an existing key: {fullPath}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(fullPath, key.ExportECPrivateKeyPem(), new UTF8Encoding(false));

        var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        Console.WriteLine("Private key written to: " + fullPath);
        Console.WriteLine();
        Console.WriteLine("Public key (paste into appsettings.json -> Updates:PublicKey):");
        Console.WriteLine(publicKey);
        Console.WriteLine();
        Console.WriteLine("WARNING: back the private key up in TWO separate, offline places.");
        Console.WriteLine("WARNING: losing it means you can never sign an update for this app again;");
        Console.WriteLine("         publish only the public key above, never the .pem file.");
        return 0;
    }

    private static int Sign(IReadOnlyDictionary<string, string> options)
    {
        if (!TryGet(options, "manifest", out var manifestPath) ||
            !TryGet(options, "key", out var keyPath) ||
            !TryGet(options, "out", out var outputPath))
        {
            return Fail("sign requires --manifest, --key and --out.");
        }

        var fullKeyPath = Path.GetFullPath(keyPath);
        if (RepositoryGuard.IsInsideRepository(fullKeyPath))
        {
            return Refused($"refusing to use a private key stored inside the repository: {fullKeyPath}");
        }

        if (!File.Exists(fullKeyPath))
        {
            return Fail($"private key not found: {fullKeyPath}");
        }

        var manifest = ReadManifest(manifestPath);
        using var key = ECDsa.Create();
        key.ImportFromPem(File.ReadAllText(fullKeyPath));

        if (key.KeySize != 256)
        {
            return Fail("the private key is not an ECDSA P-256 key.");
        }

        var signature = key.SignData(
            ManifestCanonicalizer.CanonicalBytes(manifest),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        manifest.Signature = Convert.ToBase64String(signature);

        var json = JsonSerializer.Serialize(ToDocument(manifest), SerializerOptions);
        File.WriteAllText(outputPath, json + Environment.NewLine, new UTF8Encoding(false));
        Console.WriteLine($"Signed {manifest.Version} -> {Path.GetFullPath(outputPath)}");
        return 0;
    }

    private static int Verify(IReadOnlyDictionary<string, string> options)
    {
        if (!TryGet(options, "manifest", out var manifestPath) ||
            !TryGet(options, "public-key", out var publicKeySource))
        {
            return Fail("verify requires --manifest and --public-key.");
        }

        var manifest = ReadManifest(manifestPath);

        var publicKeyText = File.Exists(publicKeySource)
            ? PublicKeyFromConfig(File.ReadAllText(publicKeySource))
            : publicKeySource.Trim();

        if (string.IsNullOrWhiteSpace(publicKeyText))
        {
            return Fail("no public key was supplied.");
        }

        IManifestVerifier verifier;
        try
        {
            verifier = new EcdsaManifestVerifier(publicKeyText);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            return Fail("the public key is not a valid base64 SPKI P-256 key.");
        }

        if (!verifier.Verify(manifest))
        {
            Console.Error.WriteLine("verification failed: signature does not match the canonical manifest.");
            return 3;
        }

        if (!UpdateManifestParser.TryParse(
                JsonSerializer.Serialize(ToDocument(manifest), SerializerOptions),
                new UpdateHostPolicy(),
                out var parsed,
                out var error))
        {
            Console.Error.WriteLine($"verification failed: {error?.ResourceKey}");
            return 3;
        }

        Console.WriteLine("Signature OK");
        Console.WriteLine($"version: {parsed.Version}");
        Console.WriteLine($"installer: {parsed.Installer.FileName} ({parsed.Installer.SizeBytes} bytes)");
        return 0;
    }

    private static UpdateManifest ReadManifest(string path)
    {
        var raw = File.ReadAllText(path);
        var document = JsonSerializer.Deserialize<ManifestDocument>(raw, SerializerOptions)
                       ?? throw new FormatException("The manifest is empty.");
        return document.ToManifest();
    }

    private static object ToDocument(UpdateManifest manifest) => new
    {
        schemaVersion = manifest.SchemaVersion,
        version = manifest.Version,
        publishedAt = ManifestCanonicalizer.FormatTimestamp(manifest.PublishedAt),
        minimumSupportedVersion = manifest.MinimumSupportedVersion,
        releaseNotes = manifest.ReleaseNotes,
        installer = new
        {
            url = manifest.Installer.Url,
            fileName = manifest.Installer.FileName,
            sizeBytes = manifest.Installer.SizeBytes,
            sha256 = manifest.Installer.Sha256,
        },
        signature = manifest.Signature,
    };

    private static string PublicKeyFromConfig(string text)
    {
        const string marker = "\"PublicKey\"";
        foreach (var line in text.Split('\n'))
        {
            var index = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            var colon = line.IndexOf(':', index);
            var comma = line.LastIndexOf(',');
            var end = comma > colon ? comma : line.Length;
            return line[(colon + 1)..end].Trim().Trim('"', ' ', '\r');
        }

        return text.Trim();
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var name = args[index];
            if (!name.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
            {
                throw new FormatException($"unexpected argument: {name}");
            }

            result[name[2..]] = args[++index];
        }

        return result;
    }

    private static bool TryGet(IReadOnlyDictionary<string, string> options, string key, out string value)
    {
        if (options.TryGetValue(key, out var found) && !string.IsNullOrWhiteSpace(found))
        {
            value = found;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("error: " + message);
        return 1;
    }

    private static int Refused(string message)
    {
        Console.Error.WriteLine("refused: " + message);
        return 2;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>An intentionally loose document shape for reading hand-written manifests.</summary>
    private sealed class ManifestDocument
    {
        public int SchemaVersion { get; set; }
        public string Version { get; set; } = string.Empty;
        public DateTimeOffset PublishedAt { get; set; }
        public string? MinimumSupportedVersion { get; set; }
        public Dictionary<string, string> ReleaseNotes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public InstallerDocument Installer { get; set; } = new();
        public string Signature { get; set; } = string.Empty;

        public UpdateManifest ToManifest() => new()
        {
            SchemaVersion = SchemaVersion,
            Version = Version,
            PublishedAt = PublishedAt,
            MinimumSupportedVersion = MinimumSupportedVersion,
            ReleaseNotes = ReleaseNotes,
            Installer = new UpdateInstaller(Installer.Url, Installer.FileName, Installer.SizeBytes, Installer.Sha256),
            Signature = Signature,
            ParsedVersion = SemanticVersion.TryParse(Version, out var parsed)
                ? parsed
                : throw new FormatException($"invalid SemVer version: {Version}"),
        };
    }

    private sealed class InstallerDocument
    {
        public string Url { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string Sha256 { get; set; } = string.Empty;
    }
}

/// <summary>Refuses to handle private keys that sit inside the repository working tree.</summary>
internal static class RepositoryGuard
{
    public static bool IsInsideRepository(string fullPath)
    {
        var root = FindRepositoryRoot(Environment.CurrentDirectory) ??
                   FindRepositoryRoot(AppContext.BaseDirectory);
        if (root is null)
        {
            return false;
        }

        return fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindRepositoryRoot(string start)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(start));
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
