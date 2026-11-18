using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using System.Xml.Linq;
using Gatehouse.Application.Identity.IdentityProviders;
using Gatehouse.Domain.Common;

namespace Gatehouse.Infrastructure.Identity;

/// <summary>Reads an IdP entity descriptor from SAML metadata XML: DTDs prohibited, no resolver, at most 1 MiB.</summary>
internal static class SamlMetadataParser
{
    private const string Md = "urn:oasis:names:tc:SAML:2.0:metadata";
    private const string Ds = "http://www.w3.org/2000/09/xmldsig#";
    private const string PostBinding = "urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST";

    public static Result<SamlMetadata> Parse(string xml)
    {
        XDocument doc;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = IdpMetadataFetcher.MaxBytes,
                MaxCharactersFromEntities = 0,
            };
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            doc = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return Invalid("SAML metadata is not valid XML (DTDs are not allowed).");
        }

        XNamespace md = Md;
        XNamespace ds = Ds;
        var entity = doc.Root?.Name == md + "EntityDescriptor" ? doc.Root : doc.Descendants(md + "EntityDescriptor").FirstOrDefault();
        var entityId = entity?.Attribute("entityID")?.Value;
        var idp = entity?.Element(md + "IDPSSODescriptor");
        if (entityId is null || idp is null)
        {
            return Invalid("SAML metadata has no IdP entity descriptor.");
        }

        var sso = idp.Elements(md + "SingleSignOnService")
            .FirstOrDefault(e => e.Attribute("Binding")?.Value == PostBinding)?
            .Attribute("Location")?.Value;
        var certText = idp.Elements(md + "KeyDescriptor")
            .Where(k => k.Attribute("use") is null || k.Attribute("use")!.Value == "signing")
            .Select(k => k.Descendants(ds + "X509Certificate").FirstOrDefault()?.Value)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        string? pem = null;
        string? thumbprint = null;
        if (certText is not null)
        {
            try
            {
                var raw = Convert.FromBase64String(string.Concat(certText.Where(c => !char.IsWhiteSpace(c))));
                using var cert = X509CertificateLoader.LoadCertificate(raw);
                pem = cert.ExportCertificatePem();
                thumbprint = cert.GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant();
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException)
            {
                return Invalid("The SAML signing certificate cannot be read.");
            }
        }

        return new SamlMetadata(entityId, sso, pem, thumbprint);
    }

    private static Error Invalid(string message) => Error.Validation("idp.metadata_invalid", message);
}
