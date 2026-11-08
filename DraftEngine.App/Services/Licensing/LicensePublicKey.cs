namespace DraftEngine.App.Services.Licensing;

/// <summary>
/// Release public key used to verify offline license keys. Only the public half is
/// embedded here; the private signing key is kept offline by the license issuer.
/// Replace this constant only when rotating the release signing key.
/// </summary>
public static class LicensePublicKey
{
    /// <summary>The RSA public key in PEM (SubjectPublicKeyInfo) form.</summary>
    public const string Pem = """
        -----BEGIN PUBLIC KEY-----
        MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEAzC3pMoe0Q/awigiZ7XnM
        NdzbaRj5WcPWY6JdPgq398IfLB30BGSO5I/Ve1BlCm5lrUnlhwAIxFtqlfZr5RrC
        X2sfosog3j1F+Rkn1yl4H/eb72I6qAK+nLMpv+2sx9HFxF2yEGiWogbM+DFK0ZM1
        CCjjfMrSqL7p/S7VHxOqNK+93FybuvWBf7dJ0LkaHyZFHtcnEEldyenaOynBKjJo
        1noRMZYRLB9KL5pbNNh4V37AsXDj01hfjwDL8yRbTtnY9s3CY9gsJSJ3Jj/QNLLC
        y6hja/w3ln32xywLQxiBHZcEpH0kvYPLncNugjGOlXtQp6C5OYsC8qR5AFEF+LdL
        cw49xfSaaMZzthngXLHwyN+uPjwZEqpwbvP4TfjB3WvmWlcb+VhxnvWO/icOLEWT
        VaVlKSAdRmGhl6IcFEtqu/dxnNsYadjhdWnaFfwI3UePSeSCR6jtO3ArtWLBWjSL
        Zw1Xq/4WoQtk5/uwm24+AEXlvRAz5gaqNg7/2+oMaHWlAgMBAAE=
        -----END PUBLIC KEY-----
        """;
}
