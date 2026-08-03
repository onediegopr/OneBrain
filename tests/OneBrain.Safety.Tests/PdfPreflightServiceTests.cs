using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OneBrain.DocumentIntelligence;

namespace OneBrain.Safety.Tests;

[TestClass]
[TestCategory("PdfPreflight")]
public sealed class PdfPreflightServiceTests
{
    [TestMethod]
    public void TextPdfRoutesToNativeExtractionWithoutOcrAuthority()
    {
        var result = PdfPreflightService.Parse(Json("text_based", 3, 0.98));

        Assert.AreEqual(PdfPreflightStatus.Ready, result.Status);
        Assert.AreEqual(PdfRecommendedTechnique.NativeTextExtraction, result.RecommendedTechnique);
        Assert.AreEqual(0, result.PagesNeedingOcr.Count);
        Assert.IsFalse(result.HumanReviewRequired);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void ComplexTextPdfRoutesToStructuredNativeExtraction()
    {
        var result = PdfPreflightService.Parse(Json("text_based", 4, 0.96, isComplex: true, tablePages: [2], columnPages: [3]));

        Assert.AreEqual(PdfPreflightStatus.Ready, result.Status);
        Assert.AreEqual(PdfRecommendedTechnique.NativeStructuredExtraction, result.RecommendedTechnique);
        CollectionAssert.AreEqual(new[] { 2 }, result.PagesWithTables.ToArray());
        CollectionAssert.AreEqual(new[] { 3 }, result.PagesWithColumns.ToArray());
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void MixedPdfRoutesOnlyAffectedPagesToOcrCandidate()
    {
        var result = PdfPreflightService.Parse(Json("mixed", 4, 0.84, ocrPages: [2, 4]));

        Assert.AreEqual(PdfPreflightStatus.Ready, result.Status);
        Assert.AreEqual(PdfRecommendedTechnique.HybridNativeAndLocalOcr, result.RecommendedTechnique);
        CollectionAssert.AreEqual(new[] { 2, 4 }, result.PagesNeedingOcr.ToArray());
        Assert.IsTrue(result.HumanReviewRequired);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void ScannedPdfRequiresEveryPageAsLocalOcrCandidate()
    {
        var result = PdfPreflightService.Parse(Json("scanned", 2, 0.95, ocrPages: [1, 2]));

        Assert.AreEqual(PdfPreflightStatus.Ready, result.Status);
        Assert.AreEqual(PdfRecommendedTechnique.LocalOcrAllPages, result.RecommendedTechnique);
        CollectionAssert.AreEqual(new[] { 1, 2 }, result.PagesNeedingOcr.ToArray());
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void ContradictoryOrUnknownOutputFailsClosed()
    {
        var unknownReason = JsonSerializer.Serialize(new
        {
            schema_version = 1,
            status = "ok",
            engine_revision = PdfInspectorSidecarOptions.PinnedEngineRevision,
            pdf_type = "mixed",
            page_count = 2,
            confidence = 0.8,
            pages_needing_ocr = new[] { 2 },
            ocr_reasons_by_page = new[] { new { page = 2, reasons = new[] { "future_unreviewed_reason" } } },
            is_complex = false,
            pages_with_tables = Array.Empty<int>(),
            pages_with_columns = Array.Empty<int>(),
            has_encoding_issues = false,
            raw_content_persisted = false,
            network_used = false,
            action_authority = false
        });
        var authorityClaim = Json("text_based", 1, 0.99, actionAuthority: true);

        var reasonResult = PdfPreflightService.Parse(unknownReason);
        var authorityResult = PdfPreflightService.Parse(authorityClaim);

        Assert.AreEqual(PdfPreflightStatus.Blocked, reasonResult.Status);
        Assert.AreEqual(PdfPreflightBlocker.ContradictoryResult, reasonResult.Blocker);
        Assert.AreEqual(PdfPreflightStatus.Blocked, authorityResult.Status);
        Assert.AreEqual(PdfPreflightBlocker.ContradictoryResult, authorityResult.Blocker);
        AssertNoAuthority(reasonResult);
        AssertNoAuthority(authorityResult);
    }

    [TestMethod]
    public async Task ServiceValidatesPdfAndBinaryHashBeforeAcceptingSidecarOutput()
    {
        using var temp = new TempDirectory();
        var pdfPath = Path.Combine(temp.Path, "fixture.pdf");
        var executablePath = Path.Combine(temp.Path, "nodal-pdf-preflight.exe");
        await File.WriteAllBytesAsync(pdfPath, "%PDF-1.4\n%%EOF"u8.ToArray());
        await File.WriteAllBytesAsync(executablePath, "pinned-sidecar-fixture"u8.ToArray());
        var executableHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(executablePath))).ToLowerInvariant();
        var runner = new FixtureRunner(new PdfInspectorProcessResult(0, Json("text_based", 1, 0.99), TimedOut: false, OutputLimitExceeded: false));
        var service = new PdfPreflightService(runner);

        var result = await service.InspectAsync(pdfPath, PdfInspectorSidecarOptions.Create(executablePath, executableHash));

        Assert.AreEqual(PdfPreflightStatus.Ready, result.Status);
        Assert.IsTrue(runner.Called);
        Assert.IsNotNull(result.SourceSha256);
        Assert.AreEqual(64, result.SourceSha256.Length);
        Assert.IsFalse(result.Summary.Contains(pdfPath, StringComparison.OrdinalIgnoreCase));
        AssertNoAuthority(result);
    }

    [TestMethod]
    public async Task HashMismatchAndTimeoutFailClosedWithoutLeakingPaths()
    {
        using var temp = new TempDirectory();
        var pdfPath = Path.Combine(temp.Path, "private-customer-name.pdf");
        var executablePath = Path.Combine(temp.Path, "nodal-pdf-preflight.exe");
        await File.WriteAllBytesAsync(pdfPath, "%PDF-1.4\n%%EOF"u8.ToArray());
        await File.WriteAllBytesAsync(executablePath, "binary"u8.ToArray());

        var hashRunner = new FixtureRunner(new PdfInspectorProcessResult(0, Json("text_based", 1, 0.99), false, false));
        var hashResult = await new PdfPreflightService(hashRunner).InspectAsync(
            pdfPath,
            PdfInspectorSidecarOptions.Create(executablePath, new string('0', 64)));

        var executableHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(executablePath))).ToLowerInvariant();
        var timeoutRunner = new FixtureRunner(new PdfInspectorProcessResult(-1, string.Empty, TimedOut: true, OutputLimitExceeded: false));
        var timeoutResult = await new PdfPreflightService(timeoutRunner).InspectAsync(
            pdfPath,
            PdfInspectorSidecarOptions.Create(executablePath, executableHash));

        Assert.AreEqual(PdfPreflightBlocker.SidecarHashInvalid, hashResult.Blocker);
        Assert.IsFalse(hashRunner.Called);
        Assert.AreEqual(PdfPreflightBlocker.SidecarTimedOut, timeoutResult.Blocker);
        Assert.IsFalse(timeoutResult.Summary.Contains("private-customer-name", StringComparison.OrdinalIgnoreCase));
        AssertNoAuthority(hashResult);
        AssertNoAuthority(timeoutResult);
    }

    [TestMethod]
    public async Task InvalidResourcePolicyFailsClosedBeforeProcessExecution()
    {
        using var temp = new TempDirectory();
        var runner = new FixtureRunner(new PdfInspectorProcessResult(0, Json("text_based", 1, 0.99), false, false));
        var options = new PdfInspectorSidecarOptions("unused.exe", new string('0', 64), 0, 0, TimeSpan.Zero, 0);

        var result = await new PdfPreflightService(runner).InspectAsync("unused.pdf", options);

        Assert.AreEqual(PdfPreflightBlocker.PolicyInvalid, result.Blocker);
        Assert.IsFalse(runner.Called);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void OcrRouterRequiresValidatedPreflightAndPreservesNoAuthority()
    {
        var request = PdfRequest();
        var missing = new OcrProviderRouter().Route(request);
        var mixedPreflight = PdfPreflightService.Parse(Json("mixed", 3, 0.82, ocrPages: [2]));
        var routed = new OcrProviderRouter().Route(request with { PdfPreflight = mixedPreflight });

        Assert.AreEqual(OcrRoutingDecisionKind.BlockDueToPdfPreflightFailure, missing.Decision);
        Assert.AreEqual(OcrRoutingDecisionKind.RecommendHybridPdfExtractionAndOcr, routed.Decision);
        Assert.AreEqual("local.pdf_inspector_preflight", routed.ProviderId);
        Assert.AreEqual(OcrProviderMode.LocalOnly, routed.ProviderMode);
        Assert.IsTrue(routed.LiveExecutionBlocked);
        Assert.IsFalse(routed.NetworkCallAllowed);
        Assert.IsFalse(routed.ActionAuthority);
    }

    private static OcrProviderRoutingRequest PdfRequest() =>
        new(
            OcrTaskType.DocumentOcr,
            OcrRiskLevel.Low,
            OcrPrivacyLevel.Normal,
            OcrCostMode.FreeOnly,
            OcrExecutionMode.FixtureOnly,
            OcrInputKind.PdfLocalFile,
            RedactionApplied: true,
            ExtractionConfidence: 0.95);

    private static string Json(
        string pdfType,
        int pageCount,
        double confidence,
        IReadOnlyList<int>? ocrPages = null,
        bool isComplex = false,
        IReadOnlyList<int>? tablePages = null,
        IReadOnlyList<int>? columnPages = null,
        bool hasEncodingIssues = false,
        bool actionAuthority = false)
    {
        ocrPages ??= [];
        var reasons = ocrPages.Select(page => new { page, reasons = new[] { "scanned" } }).ToArray();
        return JsonSerializer.Serialize(new
        {
            schema_version = 1,
            status = "ok",
            engine = "firecrawl/pdf-inspector",
            engine_revision = PdfInspectorSidecarOptions.PinnedEngineRevision,
            pdf_type = pdfType,
            page_count = pageCount,
            confidence,
            pages_needing_ocr = ocrPages,
            ocr_reasons_by_page = reasons,
            is_complex = isComplex,
            pages_with_tables = tablePages ?? [],
            pages_with_columns = columnPages ?? [],
            has_encoding_issues = hasEncodingIssues,
            processing_time_ms = 3,
            raw_content_persisted = false,
            network_used = false,
            action_authority = actionAuthority
        });
    }

    private static void AssertNoAuthority(PdfPreflightResult result)
    {
        Assert.IsFalse(result.ExecutionAuthorized);
        Assert.IsFalse(result.RawContentPersisted);
        Assert.IsFalse(result.NetworkUsed);
        Assert.IsFalse(result.ActionAuthority);
    }

    private sealed class FixtureRunner(PdfInspectorProcessResult result) : IPdfInspectorProcessRunner
    {
        public bool Called { get; private set; }

        public Task<PdfInspectorProcessResult> RunAsync(
            string executablePath,
            string pdfPath,
            long maxFileBytes,
            TimeSpan timeout,
            int maxOutputCharacters,
            CancellationToken cancellationToken)
        {
            Called = true;
            return Task.FromResult(result);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nodal-pdf-preflight-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
