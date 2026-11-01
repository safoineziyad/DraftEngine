using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DraftEngine.Core.Licensing;

namespace DraftEngine.LicenseIssuer;

/// <summary>
/// Offline license-issuing helper. The private key is never part of the application or the
/// repository; keep it on a protected machine and pass its path explicitly.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            return args[0].ToLowerInvariant() switch
            {
                "generate-key" => GenerateKey(args),
                "issue" => Issue(args),
                _ => UnknownCommand(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int GenerateKey(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("usage: DraftEngine.LicenseIssuer generate-key <private-key.pem> <public-key.pem>");
            return 1;
        }

        var privatePath = Path.GetFullPath(args[1]);
        var publicPath = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(Path.GetDirectoryName(privatePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(publicPath)!);

        using var rsa = RSA.Create(3072);
        File.WriteAllText(privatePath, rsa.ExportPkcs8PrivateKeyPem(), new UTF8Encoding(false));
        File.WriteAllText(publicPath, rsa.ExportSubjectPublicKeyInfoPem(), new UTF8Encoding(false));

        Console.WriteLine($"Private key: {privatePath}");
        Console.WriteLine($"Public key:  {publicPath}");
        Console.WriteLine();
        Console.WriteLine("Embed the public key in DraftEngine.App/Services/Licensing/LicensePublicKey.cs.");
        Console.WriteLine("Never commit the private key.");
        return 0;
    }

    private static int Issue(string[] args)
    {
        var options = ParseIssueOptions(args);
        if (options is null)
        {
            return 1;
        }

        var privateKeyPath = options.Value.PrivateKeyPath;
        if (!File.Exists(privateKeyPath))
        {
            Console.Error.WriteLine($"error: private key not found: {privateKeyPath}");
            return 1;
        }

        using var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(privateKeyPath));

        var payload = new LicensePayload
        {
            LicenseId = options.Value.LicenseId ?? Guid.NewGuid().ToString("N"),
            DisplayName = options.Value.DisplayName,
            Email = options.Value.Email,
            Product = "DraftEngine",
            Tier = options.Value.Tier,
            IssuedAt = DateTimeOffset.UtcNow,
            ExpiresAt = options.Value.ExpiresOn is { } expiresOn
                ? new DateTimeOffset(expiresOn.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero)
                : null,
        };

        Console.WriteLine(LicenseKeyCodec.CreateSignedLicense(payload, rsa));
        return 0;
    }

    private static IssueOptions? ParseIssueOptions(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"error: unexpected argument '{arg}'");
                return null;
            }

            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine($"error: missing value for '{arg}'");
                return null;
            }

            values[arg[2..]] = args[++i];
        }

        if (!values.TryGetValue("private-key", out var privateKey))
        {
            Console.Error.WriteLine("error: --private-key is required");
            return null;
        }

        if (!values.TryGetValue("email", out var email) || string.IsNullOrWhiteSpace(email))
        {
            Console.Error.WriteLine("error: --email is required");
            return null;
        }

        DateOnly? expiresOn = null;
        if (values.TryGetValue("expires-on", out var expiresText))
        {
            if (!DateOnly.TryParse(expiresText, out var parsed))
            {
                Console.Error.WriteLine("error: --expires-on must be a date, e.g. 2027-12-31");
                return null;
            }

            expiresOn = parsed;
        }

        return new IssueOptions(
            Path.GetFullPath(privateKey),
            email.Trim(),
            values.GetValueOrDefault("name", email).Trim(),
            values.GetValueOrDefault("tier", "Pro").Trim(),
            values.GetValueOrDefault("id"),
            expiresOn);
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"error: unknown command '{command}'");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            DraftEngine offline license issuer

            Generate a release key pair (run once, keep the private key secret):
              dotnet run --project tools/LicenseIssuer -- generate-key <private-key.pem> <public-key.pem>

            Issue a license key:
              dotnet run --project tools/LicenseIssuer -- issue \
                --private-key <private-key.pem> \
                --email customer@example.com \
                --name "Customer Name" \
                --tier Pro \
                --expires-on 2027-12-31
            """);
    }

    private readonly record struct IssueOptions(
        string PrivateKeyPath,
        string Email,
        string DisplayName,
        string Tier,
        string? LicenseId,
        DateOnly? ExpiresOn);
}
