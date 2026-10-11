using System.Text.Json;

namespace NeuroTune.Tests;

[TestClass]
public sealed class CompromiseAuditTests
{
    [TestMethod]
    public void Compromise_review_requires_the_audit_report_without_adding_authority()
    {
        var goals = new TuningGoals { Priority = OptimizationPriority.CompromiseReview };
        Assert.IsTrue(AuditChecklist.Required(InvestigationMode.MeasuredOptimization, goals));
        Assert.Contains("never as a verdict", LlmClient.AnalysisFocus(OptimizationPriority.CompromiseReview));
        foreach (var tool in new[] { "network-exposure", "service-integrity", "patch-level", "account-summary", "wmi-persistence", "hosts-and-proxy" })
        {
            Assert.IsTrue(InvestigationService.Tools.Any(item => item.Id == tool), tool);
            InvestigationService.Validate(new(tool, "Check exposure."));
        }
        Assert.IsFalse(InvestigationService.Tools.Any(tool => tool.Id.Contains("exploit", StringComparison.Ordinal) || tool.Id.Contains("pentest", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow(@"C:\Program Files\Vendor App\service.exe -k run", true, false)]
    [DataRow(@"""C:\Program Files\Vendor App\service.exe"" -k run", false, false)]
    [DataRow(@"C:\Windows\System32\svchost.exe -k netsvcs", false, false)]
    [DataRow(@"\SystemRoot\System32\drivers\x.sys", false, false)]
    [DataRow(@"""C:\Users\Someone\AppData\Local\Temp\dropper.exe""", false, true)]
    [DataRow(@"C:\ProgramData\Tool\tool.exe", false, true)]
    [DataRow(@"""C:\ProgramData\Microsoft\Windows Defender\Platform\4.18\MsMpEng.exe""", false, false)]
    public void Service_flags_use_path_shape_only(string path, bool unquoted, bool writable)
    {
        var flags = CompromiseAudit.ServiceFlags(path);
        Assert.AreEqual(unquoted, flags.Contains("unquoted-path-with-spaces"));
        Assert.AreEqual(writable, flags.Contains("user-writable-location"));
    }

    [TestMethod]
    [DataRow("127.0.0.1", "loopback")]
    [DataRow("::1", "loopback")]
    [DataRow("0.0.0.0", "all-interfaces")]
    [DataRow("::", "all-interfaces")]
    [DataRow("192.168.1.10", "specific-interface")]
    public void Listener_scope_is_classified(string address, string scope) => Assert.AreEqual(scope, CompromiseAudit.Scope(address));

    [TestMethod]
    public void Hardening_probes_are_audit_only_and_namespaced_for_the_audit_evidence()
    {
        Assert.IsGreaterThanOrEqualTo(10, ProbeCatalog.HardeningRegistry.Count);
        var facts = WindowsSecurityAudit.ReadSecurity();
        Assert.IsTrue(facts.Keys.Any(key => key.StartsWith("security.hardening.HKLM\\", StringComparison.Ordinal)));
    }

    [TestMethod]
    [TestCategory("WindowsIntegration")]
    public void Real_readers_return_bounded_projections_without_paths_or_names()
    {
        object[] results =
        [
            CompromiseAudit.ReadNetworkExposure(), CompromiseAudit.ReadServiceIntegrity(), CompromiseAudit.ReadPatchLevel(),
            CompromiseAudit.ReadAccountSummary(), CompromiseAudit.ReadWmiPersistence(), CompromiseAudit.ReadHostsAndProxy()
        ];
        foreach (var result in results)
        {
            var json = JsonSerializer.Serialize(result);
            Assert.IsLessThan(20_000, json.Length);
            Assert.DoesNotContain(@":\\", json);
            Assert.IsFalse(json.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
