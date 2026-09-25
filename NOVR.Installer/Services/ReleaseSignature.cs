using System.Reflection;
using System.Text;
using Org.BouncyCastle.Bcpg.OpenPgp;

namespace NOVR.Installer.Services;

// Verifies the OpenPGP signature on a release's SHA256SUMS.txt against the release-signing public key built into
// the installer (RELEASE-SIGNING-KEY.asc at the repo root, embedded at build time). Checksums alone only prove a
// download matches what was published; the signature proves the publisher holds the signing key, so a compromised
// GitHub account or release page can't push files the installer will accept.
public static class ReleaseSignature
{
    private const string PublicKeyResource = "RELEASE-SIGNING-KEY.asc";

    private static readonly Lazy<PgpPublicKeyRingBundle> TrustedKeys = new(LoadTrustedKeys);

    public static bool Verify(byte[] signedData, string armoredDetachedSignature)
    {
        try
        {
            using var signatureStream = PgpUtilities.GetDecoderStream(new MemoryStream(Encoding.ASCII.GetBytes(armoredDetachedSignature)));
            var factory = new PgpObjectFactory(signatureStream);
            var packet = factory.NextPgpObject();
            if (packet is PgpCompressedData compressed)
                packet = new PgpObjectFactory(compressed.GetDataStream()).NextPgpObject();
            if (packet is not PgpSignatureList { Count: > 0 } signatures)
                return false;

            for (var i = 0; i < signatures.Count; i++)
            {
                var signature = signatures[i];
                var key = TrustedKeys.Value.GetPublicKey(signature.KeyId);
                if (key == null) continue;

                signature.InitVerify(key);
                signature.Update(signedData);
                if (signature.Verify()) return true;
            }

            return false;
        }
        catch (Exception exception) when (exception is PgpException or IOException)
        {
            // Malformed or unexpected signature data is simply not a valid signature.
            return false;
        }
    }

    public static string TrustedKeyFingerprint
    {
        get
        {
            foreach (PgpPublicKeyRing ring in TrustedKeys.Value.GetKeyRings())
                return Convert.ToHexString(ring.GetPublicKey().GetFingerprint());
            return "(none)";
        }
    }

    private static PgpPublicKeyRingBundle LoadTrustedKeys()
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(PublicKeyResource)
                             ?? throw new InvalidOperationException("The release-signing public key is missing from this installer build.");
        return new PgpPublicKeyRingBundle(PgpUtilities.GetDecoderStream(resource));
    }
}
