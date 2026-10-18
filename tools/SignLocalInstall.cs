#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using BetterAstralParty.Packaging;
using BetterAstralParty.Updating;

namespace BetterAstralParty.GaSigning
{
    // Bounded local-only signing; no GitHub identity, credentials, installation or publication.
    public static class SignLocalInstall
    {
        private static string Stage="", PolicyPath="", ReviewPath="", Output="", Reservation="", Version="", Channel="", DescriptorHash="", ZipHash="", SourceHash="";
        private const string KeyName="BetterAstralParty.PrivateTrial.20261002", KeyId="bap-trial-20261002";
        private static void Select(string policyPath) {
            if(!policyPath.StartsWith("artifacts/automatic-update-development/",StringComparison.Ordinal) || !policyPath.EndsWith("/signing-policy.json",StringComparison.Ordinal) || policyPath.Contains('\\') || policyPath.Split('/').Any(p=>p=="" || p=="." || p==".."))
                throw new InvalidDataException("Local policy boundary required");
            PolicyPath=policyPath;Stage=policyPath.Substring(0,policyPath.LastIndexOf('/'));ReviewPath=Stage+"/signing-review.json";Output=Stage+"/signatures";
        }
        private static int invoked;
        // Protocol 5 signs only the update descriptor: one native call per version.
        private const int MaxCalls = 1;
        private sealed class Inputs { internal byte[] Descriptor=null!,Zip=null!,Proof=null!,ProofSignature=null!; }
        private sealed class Budget {
            internal readonly bool[] AttemptRecorded=new bool[MaxCalls]; internal readonly int[] Calls=new int[MaxCalls];
            internal int Total=>Calls.Sum();
            internal void Count(int index) { if(index<0 || index>=MaxCalls || !AttemptRecorded[index] || Calls[index]!=0 || Total>=MaxCalls)throw new InvalidOperationException("One durable call per descriptor, one total");Calls[index]++; }
        }
        private static string Text(JsonElement e,string name)=>e.GetProperty(name).GetString()??"";
        private static void Hash(string value) { if(value.Length!=64 || value.Any(c=>!(c>='0'&&c<='9'||c>='A'&&c<='F')))throw new InvalidDataException("Exact uppercase SHA-256 required"); }
        private static void Fields(JsonElement e,params string[] names) {
            var actual=e.EnumerateObject().Select(p=>p.Name).ToArray();
            if(actual.Length!=names.Length || !actual.ToHashSet(StringComparer.Ordinal).SetEquals(names))throw new InvalidDataException("Exact JSON fields required");
        }
        private static byte[] Read(DevelopmentPackageFiles files,string path,string hash) {
            Hash(hash);files.Capture(path);if(files.SourceHash(path)!=hash)throw new InvalidDataException("Reviewed input changed: "+path);return files.ReviewBytes(path);
        }
        private static Inputs Load(DevelopmentPackageFiles files,string root,string policyHash)
        {
            using var doc=JsonDocument.Parse(Read(files,PolicyPath,policyHash));var p=doc.RootElement;
            Fields(p,"schema","purpose","version","channel","keyName","keyId","maximumNativeSignCalls","rootIdentity","output","reservation","sourceManifestSha256","updateDescriptorSha256","zipSha256","compiledFiles","toolFiles");
            Version=Text(p,"version");Channel=Text(p,"channel");SourceHash=Text(p,"sourceManifestSha256");DescriptorHash=Text(p,"updateDescriptorSha256");ZipHash=Text(p,"zipSha256");
            CheckScope(Version,Channel);
            Reservation="artifacts/automatic-update-development/signing-"+Version+".reserved.json";
            foreach(var digest in new[]{SourceHash,DescriptorHash,ZipHash})Hash(digest);
            if(p.GetProperty("schema").GetInt32()!=1 || Text(p,"purpose")!="sign-local-install-once" || !ReleaseFeedPolicy.IsChannel(Channel)
                || UpdateVersion.Parse(Version).CompareTo(UpdateVersion.Parse(ReleaseFeedPolicy.Baseline+"-0"))<0 || !UpdateVersion.Parse(Version).IsLocalPrerelease
                || UpdateVersion.Parse(Version).Tag!=Version || Text(p,"keyName")!=KeyName || Text(p,"keyId")!=KeyId || p.GetProperty("maximumNativeSignCalls").GetInt32()!=MaxCalls
                || Text(p,"rootIdentity")!=files.RootIdentity || Text(p,"output")!=Output || Text(p,"reservation")!=Reservation)
                throw new InvalidDataException("Exact local signing policy mismatch");
            var expected=BetterAstralParty.Development.PackagingSigning.CompiledPaths(root).ToHashSet(StringComparer.Ordinal);
            var rows=p.GetProperty("compiledFiles").EnumerateArray().ToArray();
            if(rows.Length!=expected.Count || !rows.Select(x=>Text(x,"path")).ToHashSet(StringComparer.Ordinal).SetEquals(expected))throw new InvalidDataException("Exact compile closure required");
            foreach(var row in rows){Fields(row,"path","sha256");Read(files,Text(row,"path"),Text(row,"sha256"));}
            using var snapshot=JsonDocument.Parse(Read(files,Stage+"/inputs/build-source.json",SourceHash));
            var source=snapshot.RootElement.EnumerateArray().ToDictionary(x=>Text(x,"path"),x=>Text(x,"sha256"),StringComparer.Ordinal);
            foreach(var row in rows)if(!source.TryGetValue(Text(row,"path"),out var hash) || hash!=Text(row,"sha256"))throw new InvalidDataException("Compile dependency absent or different in immutable build source");
            var tools=p.GetProperty("toolFiles").EnumerateArray().ToArray();
            var expectedTools=BetterAstralParty.Development.PackagingSigning.ToolPaths(root).ToHashSet(StringComparer.Ordinal);
            if(tools.Length!=expectedTools.Count || !tools.Select(x=>Text(x,"path")).ToHashSet(StringComparer.Ordinal).SetEquals(expectedTools))throw new InvalidDataException("Exact precompiled host closure required");
            foreach(var row in tools) {
                Fields(row,"path","sha256");var path=Text(row,"path");var hash=Text(row,"sha256");Hash(hash);
                if(expected.Contains(path)){if(files.SourceHash(path)!=hash)throw new InvalidDataException("Conflicting tool hashes");}else Read(files,path,hash);
            }
            var h=new Inputs {
                Descriptor=Read(files,Stage+"/signing-inputs/update.descriptor.unsigned",DescriptorHash),
                Zip=Read(files,Stage+"/signing-inputs/payload.zip",ZipHash)
            };
            h.Proof=Read(files,"artifacts/automatic-update-development/b120/d/helper.descriptor.unsigned","07EECEF5DEF1E6E5AE95456354B719F290D75D56FBC84C066E4DE42679457856");
            h.ProofSignature=Read(files,"artifacts/automatic-update-development/b120-sign/signatures/helper.signature","E8C34656F7224D61BC05CF2D5781D209057670248F253CC22A682AB91E4829F3");
            // Historical signature proves the existing key only; it cannot authorize installation.
            var proofEnvelope=DetachedSignature.Parse(h.ProofSignature);
            UpdateTrust.Production().VerifyDetached(h.Proof,proofEnvelope.KeyId,proofEnvelope.Signature);
            if(!GaPackageContract.PublicKeyIds().SequenceEqual(new[]{KeyId}) || DetachedSignature.Parse(h.ProofSignature).KeyId!=KeyId)throw new CryptographicException("Public key pin changed");
            var owned=GaPackageContract.OwnedPaths();var sizes=new long[owned.Length];var hashes=new string[owned.Length];
            long expanded=0;
            using(var archive=new ZipArchive(new MemoryStream(h.Zip),ZipArchiveMode.Read)) {
                if(archive.Entries.Count!=owned.Length || !archive.Entries.Select(x=>x.FullName).ToHashSet(StringComparer.Ordinal).SetEquals(owned))throw new InvalidDataException("Exact current owned ZIP required");
                for(var i=0;i<owned.Length;i++){
                    var entry=archive.GetEntry(owned[i])!;
                    if(entry.Length<=0 || entry.Length>UpdateTrust.MaxFileBytes || (expanded+=entry.Length)>UpdateTrust.MaxExpandedBytes)throw new InvalidDataException("Owned ZIP expansion bound");
                    using var stream=entry.Open();var bytes=BetterAstralParty.Development.PackagingWhole.ReadEntry(stream,entry.Length);sizes[i]=bytes.Length;hashes[i]=UpdateTrust.Hash(bytes);
                }
            }
            if(!GaPackageContract.PrepareLocalInstallDescriptor(Version,Channel,h.Zip,sizes,hashes).SequenceEqual(h.Descriptor))throw new InvalidDataException("Descriptor does not match actual ZIP");
            files.Recheck();return h;
        }
        private static void CheckScope(string version,string channel) {
            var parsed=UpdateVersion.Parse(version);
            if(!ReleaseFeedPolicy.IsChannel(channel)||parsed.CompareTo(UpdateVersion.Parse(ReleaseFeedPolicy.Baseline+"-0"))<0||!parsed.IsLocalPrerelease||parsed.Tag!=version)throw new InvalidDataException("Exact local prerelease channel scope required");
        }
        private static void Review(DevelopmentPackageFiles files,string policyHash,string reviewHash) {
            using var doc=JsonDocument.Parse(Read(files,ReviewPath,reviewHash));var r=doc.RootElement;
            Fields(r,"schema","purpose","status","policySha256","maximumNativeSignCalls","automaticExecutionAuthorized","providerConfirmationPreserved","newHumanApprovalInvented","reviewReference");
            if(r.GetProperty("schema").GetInt32()!=1 || Text(r,"purpose")!="approve-local-install-signing" || Text(r,"status")!="approved"
                || Text(r,"policySha256")!=policyHash || r.GetProperty("maximumNativeSignCalls").GetInt32()!=MaxCalls
                || r.GetProperty("automaticExecutionAuthorized").ValueKind!=JsonValueKind.True || r.GetProperty("providerConfirmationPreserved").ValueKind!=JsonValueKind.True
                || r.GetProperty("newHumanApprovalInvented").ValueKind!=JsonValueKind.False || Text(r,"reviewReference").Length==0)
                throw new InvalidDataException("Independent exact automatic-signing review missing");
        }
        public static string Validate(string root,string policyPath,string policyHash,string? reviewHash) {
            Select(policyPath);using var files=new DevelopmentPackageFiles(root);Load(files,root,policyHash);if(!string.IsNullOrEmpty(reviewHash))Review(files,policyHash,reviewHash);files.Recheck();
            return JsonSerializer.Serialize(new{validated=true,policySha256=policyHash,reviewSha256=reviewHash,keyOpened=false,nativeSignCalls=0,outputsCreated=false});
        }
        private static byte[] Json(object value)=>Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value));
        private static void SignOne(Budget budget,int index,Action record,Func<byte[]> sign,Action<byte[]> verify,Action<byte[]> save) {
            record();budget.AttemptRecorded[index]=true;var signature=sign();verify(signature);save(signature);
        }
        public static string Execute(string root,string policyPath,string policyHash,string reviewHash)
        {
            Select(policyPath);Hash(policyHash);Hash(reviewHash);if(Interlocked.Exchange(ref invoked,1)!=0)throw new InvalidOperationException("No retry in this process");
            using var files=new DevelopmentPackageFiles(root);var h=Load(files,root,policyHash);Review(files,policyHash,reviewHash);files.Recheck();
            using var fence=new WindowsFileFence(root);var budget=new Budget();var stage="reserve";int? nativeStatus=null;var created=false;
            // Every local entry point for this version shares this FILE_CREATE marker before outputs or key access.
            using var marker=fence.OpenFile(Reservation,true,true);
            var scope=Json(new{schema=1,purpose="local-install-signing",policySha256=policyHash,reviewSha256=reviewHash,rootIdentity=files.RootIdentity,
                updateDescriptorSha256=DescriptorHash,zipSha256=ZipHash,
                channel=Channel,repositoryId=ReleaseFeedPolicy.For(Channel).RepositoryId,releaseId=0,assetId=0,keyName=KeyName,keyId=KeyId,maximumNativeSignCalls=MaxCalls,output=Output,
                providerConfirmationPreserved=true,newHumanApprovalInvented=false});
            marker.Stream.Write(scope,0,scope.Length);marker.Flush();
            try {
                stage="create-private-output";var directoryIdentity=files.CreateOutput(Output);created=true;files.Write("scope.json",scope);files.Recheck();
                stage="silent-key-open";using var key=CngKey.Open(KeyName,CngProvider.MicrosoftSoftwareKeyStorageProvider,CngKeyOpenOptions.Silent);
                using var handle=key.Handle;
                stage="silent-key-facts";var facts=new Dictionary<string,byte[]>();
                foreach(var name in new[]{"Name","Algorithm Group","Length","Export Policy","Key Usage","Key Type","UI Policy"})facts.Add(name,Property(handle,name));
                CheckFacts(facts);
                stage="verify-existing-key-identity";var padding=new PssPadding{Algorithm="SHA256",SaltBytes=32};var proofHash=SHA256.HashData(h.Proof);var proofSignature=DetachedSignature.Parse(h.ProofSignature).Signature;
                nativeStatus=NCryptVerifySignature(handle,ref padding,proofHash,proofHash.Length,proofSignature,proofSignature.Length,0x8|0x40);
                if(nativeStatus!=0)throw new CryptographicException("Existing key does not match public proof; no fallback",new CryptographicException(nativeStatus.Value));
                for(var index=0;index<MaxCalls;index++) {
                    var name="update";var descriptor=h.Descriptor;var descriptorHash=DescriptorHash;
                    SignOne(budget,index,()=>{
                        stage=name+"-record-attempt";files.Recheck();marker.Recheck();
                        files.Write(name+"-signing-attempt.json",Json(new{schema=1,policySha256=policyHash,reviewSha256=reviewHash,descriptorSha256=descriptorHash,maximumNativeSignCalls=1}));
                    },()=>{
                        stage=name+"-native-sign";var hash=SHA256.HashData(descriptor);var signature=new byte[384];budget.Count(index);
                        nativeStatus=NCryptSignHash(handle,ref padding,hash,hash.Length,signature,signature.Length,out var written,0x8|0x40);
                        if(nativeStatus!=0)throw new CryptographicException("Silent signing failed; preserve evidence, never retry",new CryptographicException(nativeStatus.Value));
                        if(written!=384)throw new CryptographicException("Unexpected RSA-3072 output; never retry");return signature;
                    },signature=>{
                        stage=name+"-public-verify";var envelope=new DetachedSignature(KeyId,signature).Bytes();
                        GaPackageContract.VerifyWholeUpdate(Version,h.Descriptor,envelope,h.Zip);
                    },signature=>{
                        stage=name+"-save-signature";files.Recheck();marker.Recheck();files.Write(name+".signature",new DetachedSignature(KeyId,signature).Bytes());
                    });
                }
                stage="save-evidence";var updateSignatureHash=files.OutputHash("update.signature");
                files.Write("signing-evidence.json",Json(new{schema=1,signingComplete=true,policySha256=policyHash,reviewSha256=reviewHash,updateDescriptorSha256=DescriptorHash,
                    updateSignatureSha256=updateSignatureHash,nativeSignCalls=budget.Total,perDescriptorNativeSignCalls=budget.Calls,directoryIdentity,
                    productionPublicVerification=true,providerConfirmationPreserved=true,keyCreated=false,keyExported=false,existingAclChanged=false,remoteWrites=false}));files.Recheck();marker.Recheck();
                return JsonSerializer.Serialize(new{signingComplete=true,nativeSignCalls=budget.Total,updateSignatureSha256=updateSignatureHash,output=Output});
            } catch(Exception error) {
                if(created)try{files.Write("failure.json",Json(new{schema=1,stage,nativeStatus,nativeSignCalls=budget.Total,perDescriptorNativeSignCalls=budget.Calls,exceptionType=error.GetType().FullName,hresult=error.HResult,retryAuthorized=false}));}catch{}
                throw new InvalidOperationException("Local signing stopped; preserve reservation and outputs; never rerun Execute",error);
            }
        }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        private struct PssPadding { [MarshalAs(UnmanagedType.LPWStr)] internal string Algorithm;internal int SaltBytes; }
        [DllImport("ncrypt.dll",ExactSpelling=true)]
        private static extern int NCryptSignHash(SafeNCryptKeyHandle key,ref PssPadding padding,[In]byte[] hash,int hashBytes,[Out]byte[] signature,int signatureBytes,out int written,int flags);
        [DllImport("ncrypt.dll",ExactSpelling=true)]
        private static extern int NCryptVerifySignature(SafeNCryptKeyHandle key,ref PssPadding padding,[In]byte[] hash,int hashBytes,[In]byte[] signature,int signatureBytes,int flags);
        [DllImport("ncrypt.dll",ExactSpelling=true,CharSet=CharSet.Unicode)]
        private static extern int NCryptGetProperty(SafeNCryptKeyHandle key,string property,[Out]byte[]? bytes,int byteCount,out int written,int flags);
        private const int NteNotFound=unchecked((int)0x80090011);
        private static byte[] UiPolicy(Func<byte[]?,(int Status,int Written)> query) {
            var size=query(null);if(size.Status==NteNotFound || size.Status==0&&size.Written==0)return Array.Empty<byte>();
            if(size.Status!=0)throw new CryptographicException("Silent UI policy sizing failed",new CryptographicException(size.Status));
            if(size.Written<32 || size.Written>4096)throw new CryptographicException("UI policy size bound");
            var bytes=new byte[size.Written];var data=query(bytes);
            if(data.Status!=0 || data.Written!=bytes.Length)throw new CryptographicException("Silent UI policy data failed; no fallback");return bytes;
        }
        private static byte[] Property(SafeNCryptKeyHandle key,string name) {
            if(name=="UI Policy")return UiPolicy(bytes=>{var status=NCryptGetProperty(key,name,bytes,bytes?.Length??0,out var count,0x40);return(status,count);});
            var buffer=new byte[4096];var result=NCryptGetProperty(key,name,buffer,buffer.Length,out var written,0x40);
            if(result!=0 || written<=0 || written>buffer.Length)throw new CryptographicException("Silent key property failed; no fallback");return buffer.Take(written).ToArray();
        }
        private static void CheckFacts(Dictionary<string,byte[]> facts) {
            uint Number(string name){var b=facts[name];if(b.Length!=4)throw new CryptographicException("Key number size");return BitConverter.ToUInt32(b,0);}
            string Name(string name){var b=facts[name];if(b.Length<2 || b.Length%2!=0 || b[^1]!=0 || b[^2]!=0)throw new CryptographicException("Key string size");return Encoding.Unicode.GetString(b,0,b.Length-2);}
            var ui=facts["UI Policy"];
            if(Name("Name")!=KeyName || Name("Algorithm Group")!="RSA" || Number("Length")!=3072 || Number("Export Policy")!=0 || Number("Key Usage")!=2 || Number("Key Type")!=0
                || ui.Length!=0&&(ui.Length<32 || ui.Length>4096 || BitConverter.ToUInt32(ui,0)!=1 || BitConverter.ToUInt32(ui,4)!=0))
                throw new CryptographicException("Key needs confirmation or differs from reviewed policy; no bypass");
        }
        public static string SelfCheck() {
            var passed=0;
            foreach(var channel in new[]{"Stable","PreRelease","Beta"}){CheckScope("1.4.0-rc.1.local.1",channel);passed++;}
            foreach(var pair in new[]{("1.4.0-rc.1.local.1","Prerelease"),("1.4.0","PreRelease"),("1.3.1-rc.1.local.6","PreRelease")}){var rejected=false;try{CheckScope(pair.Item1,pair.Item2);}catch(Exception error)when(error is InvalidDataException||error is UpdateValidationException){rejected=true;}if(!rejected)throw new Exception("Nonlocal or unknown local channel accepted");passed++;}
            foreach(var size in new[]{(NteNotFound,0),(0,0)}){var calls=0;if(UiPolicy(_=>{calls++;return size;}).Length!=0 || calls!=1)throw new Exception("Default UI policy");passed++;}
            var header=new byte[32];header[0]=1;var calls2=0;
            if(!UiPolicy(b=>{calls2++;if(b!=null)header.CopyTo(b,0);return(0,32);}).SequenceEqual(header)||calls2!=2)throw new Exception("Present UI policy");passed++;
            foreach(var status in new[]{5,unchecked((int)0x80090022)}){var rejected=false;try{UiPolicy(_=>(status,0));}catch(CryptographicException){rejected=true;}if(!rejected)throw new Exception("UI error accepted");passed++;}
            foreach(var data in new[]{(NteNotFound,0),(0,31)}){var count=0;var rejected=false;try{UiPolicy(_=>++count==1?(0,32):data);}catch(CryptographicException){rejected=true;}if(!rejected||count!=2)throw new Exception("UI data error accepted");passed++;}
            for(var index=0;index<MaxCalls;index++)for(var fail=-1;fail<4;fail++) {
                var budget=new Budget();var log=new List<int>();var step=0;Action next=()=>{var current=step++;log.Add(current);if(current==fail)throw new IOException("inert failure");};var stopped=false;
                try{SignOne(budget,index,next,()=>{budget.Count(index);next();return new byte[384];},_=>next(),_=>next());}catch(IOException){stopped=true;}
                if(stopped!=(fail>=0)||!log.SequenceEqual(Enumerable.Range(0,fail<0?4:fail+1))||budget.Total!=(fail==0?0:1))throw new Exception("Attempt/sign/verify/save order");passed++;
                if(budget.Total==1){var rejected=false;try{budget.Count(index);}catch(InvalidOperationException){rejected=true;}if(!rejected||budget.Total!=1)throw new Exception("Repeat call accepted");passed++;}
            }
            var noAttempt=new Budget();var refused=false;try{noAttempt.Count(0);}catch(InvalidOperationException){refused=true;}if(!refused||noAttempt.Total!=0)throw new Exception("Unrecorded attempt accepted");passed++;
            var once=new Budget();once.AttemptRecorded[0]=true;once.Count(0);
            foreach(var index in new[]{-1,0,1}){var rejected=false;try{once.Count(index);}catch(InvalidOperationException){rejected=true;}if(!rejected||once.Total!=1)throw new Exception("One call ceiling failed");passed++;}
            if(Marshal.SizeOf<PssPadding>()!=16 || Marshal.OffsetOf<PssPadding>(nameof(PssPadding.SaltBytes)).ToInt32()!=8)throw new Exception("PSS x64 layout");passed++;
            return JsonSerializer.Serialize(new{selfCheckPassed=true,cases=passed,keyOpened=false,nativeSignCalls=0,outputsCreated=false});
        }
    }
}

