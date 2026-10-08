#nullable enable
using System;
using System.Diagnostics;
using System.Reflection;

namespace BetterAstralParty.Observability
{
    internal enum DiagnosticSite { None, Unknown, PluginLoad, PluginSettings, ModUiTick, ModUiApply, CardUiUpdate, CardDiceShow, ReleaseMetadata, UpdateDownload, CoordinatorTick, CredentialPoll, HelperRun, HelperRecover, TransactionApply, TransactionRecover, RuntimeRead, RuntimeCall, NativeUi }
    internal static class SafeDiagnosticSite
    {
        // Fixed code anchors only. Never returns native addresses, file/line paths or arbitrary method names.
        internal static DiagnosticSite From(Exception? error)
        {
            if (error == null) return DiagnosticSite.None;
            try {
                var trace = new StackTrace(error, false);
                if (trace.FrameCount == 0) return DiagnosticSite.Unknown;
                for (var i = 0; i < Math.Min(16, trace.FrameCount); i++) {
                    var method = trace.GetFrame(i)?.GetMethod(); var type = method?.DeclaringType;
                    if (method == null || type == null || type.Assembly != typeof(SafeDiagnosticSite).Assembly) continue;
                    var name = method.Name;
                    if (type.DeclaringType != null && type.Name.StartsWith("<", StringComparison.Ordinal)) {
                        var end = type.Name.IndexOf('>'); if (end > 1) name = type.Name.Substring(1, end - 1); type = type.DeclaringType;
                    }
                    switch (type.FullName) {
                        case "BetterAstralParty.Plugin": if (name == "Load" || name == "LoadCore") return DiagnosticSite.PluginLoad; if (name == "RecordSettings") return DiagnosticSite.PluginSettings; break;
                        case "BetterAstralParty.ModUi": if (name == "Tick") return DiagnosticSite.ModUiTick; if (name == "Apply" || name == "ApplyCore") return DiagnosticSite.ModUiApply; break;
                        case "BetterAstralParty.CardUi": if (name == "Update") return DiagnosticSite.CardUiUpdate; break;
                        case "BetterAstralParty.CardDiceUi": if (name == "Show") return DiagnosticSite.CardDiceShow; break;
                        case "BetterAstralParty.ReleaseUpdates": if (name == "ReadAsync" || name == "ReadPageAsync") return DiagnosticSite.ReleaseMetadata; break;
                        case "BetterAstralParty.Updating.UpdateDownloads": if (name == "Read" || name == "Asset") return DiagnosticSite.UpdateDownload; break;
                        case "BetterAstralParty.AutomaticUpdates": if (name == "Tick") return DiagnosticSite.CoordinatorTick; break;
                        case "BetterAstralParty.PrivateReleaseSession": if (name == "Poll" || name == "PollCore") return DiagnosticSite.CredentialPoll; break;
                        case "BetterAstralParty.Updating.UpdateHelperService": if (name == "Run") return DiagnosticSite.HelperRun; if (name == "Recover" || name == "Failed") return DiagnosticSite.HelperRecover; break;
                        case "BetterAstralParty.Updating.UpdateTransaction": if (name == "Apply") return DiagnosticSite.TransactionApply; if (name == "Recover") return DiagnosticSite.TransactionRecover; break;
                        case "BetterAstralParty.RuntimeObject": if (name == "Get" || name == "Field" || name == "Value") return DiagnosticSite.RuntimeRead; if (name == "Call") return DiagnosticSite.RuntimeCall; break;
                        case "BetterAstralParty.NativeUi": return DiagnosticSite.NativeUi;
                    }
                }
            } catch { }
            return DiagnosticSite.Unknown;
        }
    }
}
