using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using BetterAstralParty.Packaging;
using BetterAstralParty.Updating;

namespace BetterAstralParty.Publishing;

internal sealed record TrialSigningPlan(string Repository, string Version, string Channel, string CurrentVersion,
    long ReleaseId, long ZipAssetId, string RequestDirectoryIdentity, string PublicSpkiSha256,
    string DescriptorSha256, string HelperDescriptorSha256, string ZipSha256, string HelperSha256);
internal sealed record PublisherDirectoryReceipt(string LogicalPath, string PhysicalPath, string DirectoryIdentity, string OwnerSid);

internal static class TrialSigningRequest
{
    internal const string Repository = "TSM701/BetterAstralParty", Version = "1.0.0-rc.1", KeyId = "bap-trial-20261002";
    internal static void ValidatePlan(TrialSigningPlan plan)
    {
        if (plan.Repository != Repository || plan.Version != Version || plan.Channel != "Beta" || plan.CurrentVersion != "1.0.0-dev"
            || plan.ReleaseId <= 0 || plan.ZipAssetId <= 0 || string.IsNullOrEmpty(plan.RequestDirectoryIdentity))
            throw new ArgumentException("Wrong trial plan");
        foreach (var hash in new[] { plan.PublicSpkiSha256, plan.DescriptorSha256, plan.HelperDescriptorSha256, plan.ZipSha256, plan.HelperSha256 })
            if (hash is not { Length: 64 } || hash.Any(c => c is not (>= '0' and <= '9' or >= 'A' and <= 'F')))
                throw new ArgumentException("Invalid trial hash");
    }
    internal static UpdateContext Context(TrialSigningPlan plan)
    {
        ValidatePlan(plan);
        return new UpdateContext(Repository, "Beta", "1.0.0-dev", Version, plan.ReleaseId, plan.ZipAssetId, UpdateTrust.PlatformId);
    }
    internal static void ValidateInputs(TrialSigningPlan plan, UnsignedDistribution request, string directoryIdentity)
    {
        ValidatePlan(plan);
        if (request.Context.Repository != plan.Repository || request.Context.ReleaseTag != plan.Version || request.Context.Channel != plan.Channel
            || request.Context.CurrentVersion != plan.CurrentVersion || request.Context.ReleaseId != plan.ReleaseId || request.Context.AssetId != plan.ZipAssetId
            || request.Context.Platform != UpdateTrust.PlatformId) throw new ArgumentException("Request context changed");
        if (directoryIdentity != plan.RequestDirectoryIdentity || UpdateTrust.Hash(request.Zip) != plan.ZipSha256
            || UpdateTrust.Hash(request.Descriptor) != plan.DescriptorSha256 || UpdateTrust.Hash(request.HelperDescriptor) != plan.HelperDescriptorSha256
            || UpdateTrust.Hash(request.HelperImage) != plan.HelperSha256) throw new ArgumentException("Trial bytes changed");
        // Canonical syntax and exact target/payload are checked BEFORE any persisted key is opened or signing occurs.
        var inputs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long total = 0;
        using (var zip = new ZipArchive(new MemoryStream(request.Zip, false), ZipArchiveMode.Read))
        {
            if (zip.Entries.Count != InstallReceipt.Owned.Length || request.Zip.LongLength > UpdateTrust.MaxZipBytes)
                throw new UpdateValidationException(UpdateFailure.InvalidPackage);
            foreach (var entry in zip.Entries)
            {
                if (!UpdateTrust.OwnedPath(entry.FullName) || inputs.ContainsKey(entry.FullName) || entry.Length <= 0 || entry.Length > UpdateTrust.MaxFileBytes
                    || (total += entry.Length) > UpdateTrust.MaxExpandedBytes) throw new UpdateValidationException(UpdateFailure.InvalidPackage);
                using var input = entry.Open();
                using var bytes = new MemoryStream();
                var buffer = new byte[16384]; int count;
                while ((count = input.Read(buffer)) != 0)
                {
                    if (bytes.Length + count > entry.Length) throw new UpdateValidationException(UpdateFailure.InvalidPackage);
                    bytes.Write(buffer, 0, count);
                }
                if (bytes.Length != entry.Length) throw new UpdateValidationException(UpdateFailure.InvalidPackage);
                inputs.Add(entry.FullName, bytes.ToArray());
            }
        }
        // Reconstruct the canonical request with the existing packager, without creating an unsigned "verified" type.
        var canonical = UpdateDistribution.Prepare(request.Context, inputs, request.HelperImage);
        if (!canonical.Zip.SequenceEqual(request.Zip) || !canonical.Descriptor.SequenceEqual(request.Descriptor)
            || !canonical.HelperDescriptor.SequenceEqual(request.HelperDescriptor))
            throw new UpdateValidationException(UpdateFailure.InvalidDescriptor);
    }
}

[SupportedOSPlatform("windows")]
internal static class OperationalPublisher
{
    internal const string KeyName = "BetterAstralParty.PrivateTrial.20261002", DirectoryName = "BetterAstralParty-Publisher";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    private static int Main(string[] args)
    {
        try
        {
            if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) return 65;
            if (args is ["--plan"] or ["--help"])
            {
                Console.WriteLine(JsonSerializer.Serialize(new { repository = TrialSigningRequest.Repository, version = TrialSigningRequest.Version,
                    keyName = KeyName, keyId = TrialSigningRequest.KeyId, provider = CngProvider.MicrosoftSoftwareKeyStorageProvider.Provider,
                    currentUser = true, bits = 3072, exportPolicy = "None", signingOnly = true, privateExport = false, remoteWrites = false,
                    commands = new[] { "--create-key", "--create-key-in-owned-directory <creation receipt> <receipt SHA256>", "--public <fresh output directory>", "--sign-request <request directory> <fresh output directory> <reviewed plan file> <plan SHA256>" } }, Json));
                return 0;
            }
            if (args is ["--create-key"]) { CreateKey(); return 0; }
            if (args is ["--create-key-in-owned-directory", var receipt, var receiptHash]) { ResumeKey(receipt, receiptHash); return 0; }
            if (args is ["--public", var output]) { ExportPublic(output); return 0; }
            if (args is ["--sign-request", var request, var destination, var planPath, var planHash]) { Sign(request, destination, planPath, planHash); return 0; }
            Console.Error.WriteLine("BAP-PUBLISHER:InvalidArguments"); return 64;
        }
        catch (DuplicatePublisherState) { Console.Error.WriteLine("BAP-PUBLISHER:AlreadyExistsNoChanges"); return 10; }
        catch (UpdateValidationException error) { Console.Error.WriteLine("BAP-PUBLISHER:" + error.Failure); return 2; }
        catch (ApplySafetyException error) { Console.Error.WriteLine("BAP-PUBLISHER:" + error.Failure + ":" + error.NativeError); return 5; }
        catch { Console.Error.WriteLine("BAP-PUBLISHER:FailedNoSecretDetails"); return 6; }
    }
    private sealed class DuplicatePublisherState : Exception { }
    private static string LocalDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DirectoryName);
    [StructLayout(LayoutKind.Sequential)]
    private struct DirectorySecurityAttributes { internal int Length; internal IntPtr Descriptor; internal int Inherit; }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectoryW(string path, ref DirectorySecurityAttributes security);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern uint GetFinalPathNameByHandleW(Microsoft.Win32.SafeHandles.SafeFileHandle handle, StringBuilder path, uint length, uint flags);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)]
    private static extern int GetCurrentPackageFamilyName(ref uint length, StringBuilder? name);
    private static string ExpectedPhysicalDirectory()
    {
        uint length=0; var status=GetCurrentPackageFamilyName(ref length,null);
        if(status==15700) return LocalDirectory; // Unpackaged host.
        if(status!=122 || length is <2 or >256) throw new InvalidOperationException("Package identity unavailable");
        var name=new StringBuilder(checked((int)length));
        if(GetCurrentPackageFamilyName(ref length,name)!=0) throw new InvalidOperationException("Package identity unavailable");
        var family=name.ToString();
        if(family.Any(c=>!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-'))) throw new InvalidOperationException("Unexpected package family");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Packages",family,"LocalCache","Local",DirectoryName);
    }
    private static string ResolvePublisherDirectory(string? approvedPhysicalDirectory=null)
    {
        using var handle=CreateFileW(LocalDirectory,0x20080,1,IntPtr.Zero,3,0x02200000,IntPtr.Zero); // OPEN_EXISTING + OPEN_REPARSE_POINT.
        if(handle.IsInvalid) throw new ApplySafetyException(ApplyFailure.UnsafePath,Marshal.GetLastWin32Error());
        var path=new StringBuilder(512);var count=GetFinalPathNameByHandleW(handle,path,512,0);
        if(count==0 || count>=512) throw new ApplySafetyException(ApplyFailure.UnsafePath);
        var physical=path.ToString();if(physical.StartsWith(@"\\?\",StringComparison.Ordinal)) physical=physical[4..];
        if(!string.Equals(physical,LocalDirectory,StringComparison.OrdinalIgnoreCase) && !string.Equals(physical,ExpectedPhysicalDirectory(),StringComparison.OrdinalIgnoreCase)
            && !string.Equals(physical,approvedPhysicalDirectory,StringComparison.OrdinalIgnoreCase))
            throw new ApplySafetyException(ApplyFailure.IdentityChanged);
        // The resolved physical path must pass the SAME strict fence, not an alias-tolerant updater boundary.
        using var parent=new WindowsFileFence(Path.GetDirectoryName(physical)!);
        parent.AssertPrivateDirectory(DirectoryName); parent.Recheck();
        return physical;
    }
    private static string CreatePublisherDirectory(WindowsFileFence parent)
    {
        // Win32 CREATE_NEW under pinned, rechecked ancestors; never follow/adopt an existing leaf.
        // NT relative creation is rejected by this host's profile filesystem (4395); updater/staging primitives stay unchanged.
        if (Path.GetDirectoryName(LocalDirectory) != parent.Root) throw new ArgumentException("Unexpected publisher parent");
        parent.Recheck();
        using var descriptor = new WindowsPrivateSecurity.Descriptor();
        var security = new DirectorySecurityAttributes { Length=Marshal.SizeOf<DirectorySecurityAttributes>(), Descriptor=descriptor.Pointer };
        if (!CreateDirectoryW(LocalDirectory, ref security))
        {
            var code=Marshal.GetLastWin32Error();
            if (code is 80 or 183) throw new DuplicatePublisherState();
            throw new ApplySafetyException(ApplyFailure.UnsafePath, code);
        }
        // MSIX maps logical AppData writes into the current package's LocalCache. Only that OS-derived mapping is accepted.
        var physical=ResolvePublisherDirectory(); parent.Recheck(); return physical;
    }
    private static CngKey OpenKey()
    {
        var key = CngKey.Open(KeyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.None);
        try { ValidateKey(key); return key; } catch { key.Dispose(); throw; }
    }
    private static void ValidateKey(CngKey key)
    {
        if (key.IsEphemeral || key.IsMachineKey || key.KeyName != KeyName || key.Provider != CngProvider.MicrosoftSoftwareKeyStorageProvider
            || key.ExportPolicy != CngExportPolicies.None || key.KeyUsage != CngKeyUsages.Signing || key.AlgorithmGroup != CngAlgorithmGroup.Rsa)
            throw new CryptographicException("Unexpected key policy");
        using var rsa = new RSACng(key);
        var parameters = rsa.ExportParameters(false);
        if (rsa.KeySize != 3072 || parameters.Exponent is not [1, 0, 1]) throw new CryptographicException("Unexpected key size");
    }
    private static Dictionary<string, byte[]> PublicEvidence(CngKey key, bool created)
    {
        ValidateKey(key);
        using var rsa = new RSACng(key);
        var parameters = rsa.ExportParameters(false);
        var spki = rsa.ExportSubjectPublicKeyInfo();
        if (!spki.SequenceEqual(ProductionUpdateKeys.Spki(parameters))) throw new CryptographicException("Independent public encoding mismatch");
        var fingerprint = UpdateTrust.Hash(spki);
        ProductionUpdateKeys.Review(TrialSigningRequest.KeyId, Convert.ToBase64String(parameters.Modulus!), fingerprint);
        using var identity = WindowsIdentity.GetCurrent();
        var publicRecord = JsonSerializer.SerializeToUtf8Bytes(new { keyId = TrialSigningRequest.KeyId, keyName = KeyName, created,
            provider = key.Provider!.Provider, currentUser = true, sid = identity.User?.Value, bits = rsa.KeySize,
            exponent = 65537, exportPolicy = key.ExportPolicy.ToString(), keyUsage = key.KeyUsage.ToString(),
            algorithm = "RSA-PSS/SHA-256", saltBytes = 32, spkiSha256 = fingerprint,
            modulusBase64 = Convert.ToBase64String(parameters.Modulus!), publicOnly = true, independentBclSpkiEqualsManualDer = true,
            privateExportAttempted = false, privateKeyFilesystemRead = false, tokenAccess = false,
            keyStorageDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Crypto", "Keys"),
            protectionLimit = "Same-user processes are not isolated. Key loss requires manual rebootstrap." }, Json);
        return new() { { "public.spki.der", spki }, { "public-key.json", publicRecord } };
    }
    private static void CreateKey()
    {
        // No overwrite/reuse/deletion. A partial failure remains for manual investigation; never silently retry creation.
        if (CngKey.Exists(KeyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.None)
            || Directory.Exists(LocalDirectory) || File.Exists(LocalDirectory)) throw new DuplicatePublisherState();
        var parent = Path.GetDirectoryName(LocalDirectory)!;
        using var fence = new WindowsFileFence(parent);
        var physical=CreatePublisherDirectory(fence);
        CreateKeyAndPublicEvidence(physical);
        fence.Recheck();
    }
    private static void ResumeKey(string receiptPath, string expectedHash)
    {
        if(CngKey.Exists(KeyName,CngProvider.MicrosoftSoftwareKeyStorageProvider,CngKeyOpenOptions.None)) throw new DuplicatePublisherState();
        var path=Path.GetFullPath(receiptPath);PublisherDirectoryReceipt receipt;
        using(var source=new WindowsFileFence(Path.GetDirectoryName(path)!)) using(var file=source.OpenFile(Path.GetFileName(path)))
        {
            var bytes=UpdateTicket.Read(file,4096);
            if(UpdateTrust.Hash(bytes)!=expectedHash) throw new ArgumentException("Unreviewed directory receipt");
            receipt=JsonSerializer.Deserialize<PublisherDirectoryReceipt>(bytes,Json) ?? throw new ArgumentException("Missing directory receipt");
        }
        using var identity=WindowsIdentity.GetCurrent();
        // A recorded OS-created directory identity is required when an unpackaged child inherits brokered MSIX filesystem mapping.
        var local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var packageRoot=Path.Combine(local,"Packages")+Path.DirectorySeparatorChar;
        if(!receipt.PhysicalPath.StartsWith(packageRoot,StringComparison.OrdinalIgnoreCase)
            || !receipt.PhysicalPath.EndsWith(Path.DirectorySeparatorChar+"LocalCache"+Path.DirectorySeparatorChar+"Local"+Path.DirectorySeparatorChar+DirectoryName,StringComparison.Ordinal))
            throw new ArgumentException("Unexpected package-local receipt");
        var resolved=ResolvePublisherDirectory(receipt.PhysicalPath);
        if(receipt.LogicalPath!=LocalDirectory || receipt.PhysicalPath!=resolved || receipt.OwnerSid!=identity.User?.Value) throw new ArgumentException("Unexpected owned directory");
        using var parent=new WindowsFileFence(Path.GetDirectoryName(resolved)!);
        parent.AssertPrivateDirectory(DirectoryName);
        if(parent.DirectoryIdentity(DirectoryName).Text!=receipt.DirectoryIdentity) throw new ApplySafetyException(ApplyFailure.IdentityChanged);
        CreateKeyAndPublicEvidence(resolved);parent.Recheck();
    }
    private static void CreateKeyAndPublicEvidence(string physicalDirectory)
    {
        using var fence=new WindowsFileFence(Path.GetDirectoryName(physicalDirectory)!);
        fence.AssertPrivateDirectory(DirectoryName);
        if (CngKey.Exists(KeyName, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.None)) throw new DuplicatePublisherState();
        var options = new CngKeyCreationParameters { Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
            ExportPolicy = CngExportPolicies.None, KeyUsage = CngKeyUsages.Signing, KeyCreationOptions = CngKeyCreationOptions.None };
        options.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(3072), CngPropertyOptions.None));
        using var key = CngKey.Create(CngAlgorithm.Rsa, KeyName, options);
        var evidence = PublicEvidence(key, true);
        foreach (var file in evidence)
        {
            var relative=DirectoryName + "/" + file.Key;
            using var target=fence.OpenFile(relative,true,true);
            target.Stream.Write(file.Value);target.Flush();
            if(target.Hash()!=UpdateTrust.Hash(file.Value)) throw new ApplySafetyException(ApplyFailure.IdentityChanged);
        }
        fence.Recheck(); Console.WriteLine("BAP-PUBLISHER:CreatedPublicEvidenceOnly");
    }
    private static void WriteNew(string output, IDictionary<string, byte[]> files)
    {
        var full = Path.GetFullPath(output);
        var parent = Path.GetDirectoryName(full) ?? throw new ArgumentException("Output parent required");
        var leaf = Path.GetFileName(full);
        using var fence = new WindowsFileFence(parent);
        fence.CreatePrivateDirectory(leaf);
        using var destination = new WindowsFileFence(full);
        UpdateDistribution.WriteNew(destination, files);
        destination.Recheck(); fence.Recheck();
    }
    private static void ExportPublic(string output)
    {
        using var key = OpenKey(); WriteNew(output, PublicEvidence(key, false)); Console.WriteLine("BAP-PUBLISHER:PublicOnly");
    }
    private static void Sign(string requestRoot, string output, string planPath, string expectedPlanHash)
    {
        var fullPlan = Path.GetFullPath(planPath);
        TrialSigningPlan plan;
        using (var planFence = new WindowsFileFence(Path.GetDirectoryName(fullPlan)!))
        using (var file = planFence.OpenFile(Path.GetFileName(fullPlan)))
        {
            var bytes = UpdateTicket.Read(file, 16384);
            if (UpdateTrust.Hash(bytes) != expectedPlanHash) throw new ArgumentException("Unreviewed plan");
            plan = JsonSerializer.Deserialize<TrialSigningPlan>(bytes, Json) ?? throw new ArgumentException("Missing plan");
        }
        var context = TrialSigningRequest.Context(plan);
        using var source = new WindowsFileFence(requestRoot);
        var request = UpdateDistribution.ReadRequest(source, context);
        TrialSigningRequest.ValidateInputs(plan, request, source.RootIdentity.Text);
        if (!UpdateTrust.Production().Configured) throw new UpdateValidationException(UpdateFailure.NotConfigured);
        using var key = OpenKey();
        using var rsa = new RSACng(key);
        var spki = rsa.ExportSubjectPublicKeyInfo();
        if (UpdateTrust.Hash(spki) != plan.PublicSpkiSha256) throw new ArgumentException("Unexpected public key");
        var pinned = ProductionUpdateKeys.Read().Single(k => k.Id == TrialSigningRequest.KeyId);
        if (!ProductionUpdateKeys.Spki(pinned.Parameters()).SequenceEqual(spki)) throw new ArgumentException("Unpinned signing key");
        var updateSignature = new DetachedSignature(TrialSigningRequest.KeyId, rsa.SignData(request.Descriptor, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)).Bytes();
        var helperSignature = new DetachedSignature(TrialSigningRequest.KeyId, rsa.SignData(request.HelperDescriptor, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)).Bytes();
        // Verify both signatures and complete payload before output creation. No signing of arbitrary bytes.
        var finalized = UpdateDistribution.Finalize(request, updateSignature, helperSignature, UpdateTrust.Production());
        source.Recheck();
        WriteNew(output, new Dictionary<string, byte[]> { { "update.signature", updateSignature }, { "helper.signature", helperSignature },
            { "signing-evidence.json", JsonSerializer.SerializeToUtf8Bytes(new { planSha256 = expectedPlanHash, publicSpkiSha256 = plan.PublicSpkiSha256,
                plan.ReleaseId, plan.ZipAssetId, plan.DescriptorSha256, plan.HelperDescriptorSha256, plan.ZipSha256, plan.HelperSha256,
                outputFiles = finalized.Keys.OrderBy(v => v, StringComparer.Ordinal).ToArray(), verified = true, privateExport = false, remoteWrites = false }, Json) } });
        Console.WriteLine("BAP-PUBLISHER:TrialSignaturesVerified");
    }
}
