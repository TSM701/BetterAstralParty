#nullable enable
using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace BetterAstralParty.Updating
{
    // Only reviewed PUBLIC parameters belong here. No runtime file/env/config trust import.
    internal static class ProductionUpdateKeys
    {
        internal static TrustedUpdateKey[] Read() { return new[] { Review("bap-trial-20261002", "tlLGH7E3AsD6TG6wy5UOUxRhcHTcxWdhAqalYs+JbXTsnqhn1enMYMOKG4FBphmVkTBis6Vf/kXZqsd27b/h/jBcUFY6OXQ0YP52o3F9pjCvRrekRN+Jx8n4u+zpCMRn1lEJ3O6PBrlOBCdNDnvon4t43OkGwo7U1O1X7j0YIylZl5WIl+LDtWhEtEin1za6Nuso25UT0uLjm0sDHZa+9MJS1U3J5sF/BPGSm4DDcUUe3ly6qz78A83dPdz0LcBBQfezxFfxCu1gs5vIAx8+OeR5kXYeSnchY7/XTZGiPRXU2AdMyPv98XDeidI2mcEOJpteOMcHPSq8KcmX1J1B3C+QgufiZ7N/EU82ZePkJxCCIfqJroy4L9pxGRMNSkCdPaXw6LV7b/wEye48pMQ/Ums38pbE+QOp13QfBcTrOW9GSUKTAvLUyMK+ACKHaR44NOC1frCNTebokSVokoKgJDrOHrRRv6U1y/dslRBptwURtxzFq/Nuf61V0b5hR9fh", "99ACE415879EF2FCA89C741F4F93838D8469953A4787C47C0C1A16BEF0B2A2E7") }; }
        internal static TrustedUpdateKey Review(string id, string modulusBase64, string expectedSpkiSha256)
        {
            if (modulusBase64 == null || (modulusBase64.Length != 512 && modulusBase64.Length != 684)) throw new ArgumentException("Invalid public pin");
            var modulus = Convert.FromBase64String(modulusBase64);
            if (Convert.ToBase64String(modulus) != modulusBase64) throw new ArgumentException("Invalid public pin");
            var parameters = new RSAParameters { Modulus = modulus, Exponent = new byte[] { 1, 0, 1 } };
            var key = new TrustedUpdateKey(id, parameters);
            if (expectedSpkiSha256 == null || expectedSpkiSha256.Length != 64 || UpdateTrust.Hash(Spki(parameters)) != expectedSpkiSha256)
                throw new ArgumentException("Public fingerprint mismatch");
            return key;
        }
        internal static byte[] Spki(RSAParameters parameters)
        {
            // Validate public-only RSA 3072/4096 and exponent before canonical DER encoding.
            new TrustedUpdateKey("public-review", parameters);
            var rsa = Der(48, Concat(Integer(parameters.Modulus!), Integer(parameters.Exponent!)));
            var algorithm = new byte[] { 48,13,6,9,42,134,72,134,247,13,1,1,1,5,0 };
            return Der(48, Concat(algorithm, Der(3, Concat(new byte[] { 0 }, rsa))));
        }
        private static byte[] Integer(byte[] bytes) { return Der(2, (bytes[0] & 128) != 0 ? Concat(new byte[] { 0 }, bytes) : bytes); }
        private static byte[] Concat(params byte[][] arrays)
        {
            var bytes = new List<byte>(); foreach (var array in arrays) bytes.AddRange(array); return bytes.ToArray();
        }
        private static byte[] Der(byte tag, byte[] bytes)
        {
            var output = new List<byte> { tag };
            if (bytes.Length < 128) output.Add((byte)bytes.Length);
            else if (bytes.Length < 256) { output.Add(129); output.Add((byte)bytes.Length); }
            else { output.Add(130); output.Add((byte)(bytes.Length >> 8)); output.Add((byte)bytes.Length); }
            output.AddRange(bytes); return output.ToArray();
        }
    }
}
