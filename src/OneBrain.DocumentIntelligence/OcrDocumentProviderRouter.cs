using System.Text.RegularExpressions;

namespace OneBrain.DocumentIntelligence;

public enum OcrProviderKind
{
    DeterministicPdfInspector,
    LocalOcr,
    PaidOcr,
    DocumentAi,
    VisualLanguageModel,
    HumanReview
}

public enum OcrProviderMode
{
    Disabled,
    FixtureOnly,
    LocalOnly,
    DesignOnly,
    LiveCandidateBlocked
}

public enum OcrInputKind
{
    ImageFixture,
    PdfFixture,
    PdfLocalFile,
    DocumentFixture,
    ScreenshotCropFixture
}

public enum OcrOutputKind
{
    RoutingMetadata,
    Text,
    Markdown,
    StructuredBlocks,
    TableBlocks,
    KeyValueFields,
    RedactionCandidates
}

public enum OcrProviderCapability
{
    PdfPreflightClassification,
    PageLevelOcrRouting,
    EncodingIssueDetection,
    TextExtraction,
    LayoutDetection,
    BoundingBoxes,
    ConfidenceScores,
    TableExtraction,
    MarkdownOutput,
    KeyValueExtraction,
    DocumentClassification,
    HandwritingCandidate,
    SignatureRegionCandidate
}

public enum OcrForbiddenCapability
{
    ActionAuthorization,
    CaptchaSolving,
    LoginExecution,
    PaymentExecution,
    FiscalSubmission,
    IrreversibleAction,
    BrowserControl,
    DesktopControl
}

public enum OcrTaskType
{
    ScreenPerception,
    DocumentOcr,
    InvoiceExtraction,
    ReceiptExtraction,
    FiscalDocumentExtraction,
    TableExtraction,
    GenericDocumentUnderstanding
}

public enum OcrRiskLevel
{
    Low,
    Medium,
    High,
    Regulated
}

public enum OcrPrivacyLevel
{
    Normal,
    Sensitive,
    Regulated
}

public enum OcrCostMode
{
    FreeOnly,
    AllowPaidCandidate,
    PaidLiveBlocked
}

public enum OcrExecutionMode
{
    FixtureOnly,
    DesignOnly,
    LiveBlocked
}

public enum OcrRoutingDecisionKind
{
    RecommendNativePdfExtractionCandidate,
    RecommendHybridPdfExtractionAndOcr,
    RecommendLocalOcrForPdfPages,
    BlockDueToPdfPreflightFailure,
    UseLocalOcrFixture,
    RecommendMistralOcr4Candidate,
    RecommendMistralDocumentAiCandidate,
    RequireHumanReview,
    BlockLiveProvider,
    BlockDueToSensitiveUnredactedInput,
    BlockDueToActionAuthorityRequest,
    BlockDueToNetworkNotAllowed,
    BlockDueToPaidProviderNotEnabled
}

public enum OcrConfidenceBand
{
    Missing,
    Low,
    Medium,
    High
}

public enum OcrEvidenceBlockType
{
    Text,
    Markdown,
    Table,
    KeyValue,
    RedactionCandidate,
    BoundingBox
}

public sealed record OcrProviderPolicy(
    string ConfidencePolicy,
    string RedactionPolicy,
    string EvidencePolicy,
    string HumanReviewPolicy,
    string CostPolicy,
    string PrivacyPolicy,
    string NetworkPolicy,
    bool RequiresApiKey,
    bool NetworkCallsEnabled,
    bool LiveExecutionEnabled,
    bool HasLiveClient);

public sealed record OcrProviderDescriptor(
    string ProviderId,
    OcrProviderKind ProviderKind,
    OcrProviderMode Mode,
    IReadOnlySet<OcrInputKind> SupportedInputs,
    IReadOnlySet<OcrOutputKind> SupportedOutputs,
    IReadOnlySet<OcrProviderCapability> Capabilities,
    IReadOnlySet<OcrForbiddenCapability> ForbiddenCapabilities,
    OcrProviderPolicy Policy);

public sealed record OcrProviderRoutingRequest(
    OcrTaskType TaskType,
    OcrRiskLevel RiskLevel,
    OcrPrivacyLevel PrivacyLevel,
    OcrCostMode CostMode,
    OcrExecutionMode ExecutionMode,
    OcrInputKind InputKind,
    bool RedactionApplied,
    bool RequestsActionAuthority = false,
    bool RequestsBrowserControl = false,
    bool RequestsDesktopControl = false,
    bool ContainsCaptchaLikeChallenge = false,
    bool ContainsLoginLikeFlow = false,
    bool ContainsPaymentLikeFlow = false,
    bool ContainsFiscalSubmissionLikeFlow = false,
    bool ConflictingFieldsDetected = false,
    double? ExtractionConfidence = null)
{
    public PdfPreflightResult? PdfPreflight { get; init; }
}

public sealed record OcrConfidencePolicy(
    double HighThreshold = 0.90,
    double MediumThreshold = 0.70)
{
    public OcrConfidenceEvaluation Evaluate(double? confidence)
    {
        if (!confidence.HasValue)
        {
            return new OcrConfidenceEvaluation(OcrConfidenceBand.Missing, HumanReviewRequired: true, "Missing confidence requires human review.");
        }

        if (confidence.Value >= HighThreshold)
        {
            return new OcrConfidenceEvaluation(OcrConfidenceBand.High, HumanReviewRequired: false, "High-confidence observation.");
        }

        if (confidence.Value >= MediumThreshold)
        {
            return new OcrConfidenceEvaluation(OcrConfidenceBand.Medium, HumanReviewRequired: true, "Medium confidence remains review-recommended.");
        }

        return new OcrConfidenceEvaluation(OcrConfidenceBand.Low, HumanReviewRequired: true, "Low confidence requires human review.");
    }
}

public sealed record OcrConfidenceEvaluation(
    OcrConfidenceBand Band,
    bool HumanReviewRequired,
    string Reason);

public sealed record OcrProviderRoutingDecision(
    OcrRoutingDecisionKind Decision,
    string? ProviderId,
    OcrProviderMode ProviderMode,
    bool HumanReviewRequired,
    bool LiveExecutionBlocked,
    bool PaidExecutionBlocked,
    bool NetworkCallAllowed,
    bool ActionAuthority,
    IReadOnlyList<string> Reasons,
    OcrConfidenceEvaluation Confidence);

public sealed record OcrBoundingBox(int PageNumber, double X, double Y, double Width, double Height);

public sealed record OcrRedactionCandidate(
    string Category,
    int PageNumber,
    string BlockId,
    OcrBoundingBox? BoundingBox,
    double Confidence);

public sealed record OcrEvidenceBlock(
    string BlockId,
    OcrEvidenceBlockType BlockType,
    int PageNumber,
    OcrBoundingBox? BoundingBox,
    double? Confidence,
    string RedactedText,
    bool RawTextPresent,
    bool HumanReviewRequired,
    bool ActionAuthority);

public sealed record OcrEvidencePack(
    string EvidenceId,
    string FixtureId,
    string SourceHash,
    string ProviderId,
    OcrProviderMode ProviderMode,
    IReadOnlyList<OcrEvidenceBlock> Blocks,
    IReadOnlyList<OcrRedactionCandidate> RedactionCandidates,
    OcrProviderRoutingDecision PolicyDecision,
    bool RawScreenshotStored,
    bool RawDocumentStored,
    bool HumanReviewRequired,
    bool ActionAuthority,
    string Summary);

public sealed record OcrFixtureDocument(
    string FixtureId,
    OcrTaskType TaskType,
    OcrInputKind InputKind,
    OcrRiskLevel RiskLevel,
    OcrPrivacyLevel PrivacyLevel,
    bool RedactionApplied,
    bool ContainsSensitiveRawText,
    bool ContainsConflictingFields,
    bool ContainsCaptchaLikeChallenge,
    bool ContainsLoginLikeFlow,
    bool ContainsPaymentLikeFlow,
    bool ContainsFiscalSubmissionLikeFlow,
    double? Confidence,
    string SyntheticText);

public sealed class OcrProviderRegistry
{
    private static readonly IReadOnlySet<OcrForbiddenCapability> StandardForbidden = Enum.GetValues<OcrForbiddenCapability>().ToHashSet();

    public IReadOnlyList<OcrProviderDescriptor> Providers { get; } =
    [
        Provider(
            "local.pdf_inspector_preflight",
            OcrProviderKind.DeterministicPdfInspector,
            OcrProviderMode.LocalOnly,
            Set(OcrInputKind.PdfLocalFile),
            Set(OcrOutputKind.RoutingMetadata),
            Set(
                OcrProviderCapability.PdfPreflightClassification,
                OcrProviderCapability.PageLevelOcrRouting,
                OcrProviderCapability.EncodingIssueDetection,
                OcrProviderCapability.LayoutDetection),
            "pinned local PDF preflight; no extraction, persistence, network, OCR, or authority"),
        Provider(
            "local.onnx_ocr_fixture",
            OcrProviderKind.LocalOcr,
            OcrProviderMode.FixtureOnly,
            Set(OcrInputKind.ImageFixture, OcrInputKind.PdfFixture, OcrInputKind.DocumentFixture, OcrInputKind.ScreenshotCropFixture),
            Set(OcrOutputKind.Text, OcrOutputKind.StructuredBlocks, OcrOutputKind.RedactionCandidates),
            Set(OcrProviderCapability.TextExtraction, OcrProviderCapability.BoundingBoxes, OcrProviderCapability.ConfidenceScores),
            "local fixture OCR candidate; no network"),
        Provider(
            "cloud.mistral_ocr_4",
            OcrProviderKind.PaidOcr,
            OcrProviderMode.LiveCandidateBlocked,
            Set(OcrInputKind.PdfFixture, OcrInputKind.DocumentFixture, OcrInputKind.ImageFixture),
            Set(OcrOutputKind.Text, OcrOutputKind.Markdown, OcrOutputKind.StructuredBlocks, OcrOutputKind.TableBlocks, OcrOutputKind.RedactionCandidates),
            Set(
                OcrProviderCapability.TextExtraction,
                OcrProviderCapability.LayoutDetection,
                OcrProviderCapability.BoundingBoxes,
                OcrProviderCapability.ConfidenceScores,
                OcrProviderCapability.TableExtraction,
                OcrProviderCapability.MarkdownOutput,
                OcrProviderCapability.DocumentClassification,
                OcrProviderCapability.HandwritingCandidate),
            "Mistral OCR 4 paid provider candidate; design-only live blocked"),
        Provider(
            "cloud.mistral_document_ai",
            OcrProviderKind.DocumentAi,
            OcrProviderMode.LiveCandidateBlocked,
            Set(OcrInputKind.PdfFixture, OcrInputKind.DocumentFixture),
            Set(OcrOutputKind.Markdown, OcrOutputKind.StructuredBlocks, OcrOutputKind.TableBlocks, OcrOutputKind.KeyValueFields, OcrOutputKind.RedactionCandidates),
            Set(
                OcrProviderCapability.TextExtraction,
                OcrProviderCapability.LayoutDetection,
                OcrProviderCapability.BoundingBoxes,
                OcrProviderCapability.ConfidenceScores,
                OcrProviderCapability.TableExtraction,
                OcrProviderCapability.MarkdownOutput,
                OcrProviderCapability.KeyValueExtraction,
                OcrProviderCapability.DocumentClassification,
                OcrProviderCapability.SignatureRegionCandidate),
            "Mistral Document AI provider candidate; design-only live blocked"),
        Provider(
            "human.review",
            OcrProviderKind.HumanReview,
            OcrProviderMode.FixtureOnly,
            Set(OcrInputKind.ImageFixture, OcrInputKind.PdfFixture, OcrInputKind.DocumentFixture, OcrInputKind.ScreenshotCropFixture),
            Set(OcrOutputKind.Text, OcrOutputKind.RedactionCandidates),
            Set<OcrProviderCapability>(),
            "human review fallback")
    ];

    public OcrProviderDescriptor GetRequired(string providerId) =>
        Providers.First(provider => string.Equals(provider.ProviderId, providerId, StringComparison.Ordinal));

    private static OcrProviderDescriptor Provider(
        string providerId,
        OcrProviderKind kind,
        OcrProviderMode mode,
        IReadOnlySet<OcrInputKind> inputs,
        IReadOnlySet<OcrOutputKind> outputs,
        IReadOnlySet<OcrProviderCapability> capabilities,
        string description) =>
        new(
            providerId,
            kind,
            mode,
            inputs,
            outputs,
            capabilities,
            StandardForbidden,
            new OcrProviderPolicy(
                "Confidence is observation-only and cannot authorize actions.",
                "Sensitive or regulated cloud routing requires redaction and human review.",
                "Evidence stores fixture ids, hashes, redacted blocks, policy decisions, and metadata only.",
                "Low, missing, conflicting, sensitive, or regulated output requires human review.",
                "Paid providers are candidates only; paid execution remains blocked.",
                "No sensitive unredacted document leaves fixture-safe boundary.",
                $"No live network calls: {description}.",
                RequiresApiKey: false,
                NetworkCallsEnabled: false,
                LiveExecutionEnabled: false,
                HasLiveClient: false));

    private static IReadOnlySet<T> Set<T>(params T[] values) where T : notnull => values.ToHashSet();
}

public sealed class OcrProviderRouter
{
    private readonly OcrProviderRegistry _registry;
    private readonly OcrConfidencePolicy _confidencePolicy;

    public OcrProviderRouter(OcrProviderRegistry? registry = null, OcrConfidencePolicy? confidencePolicy = null)
    {
        _registry = registry ?? new OcrProviderRegistry();
        _confidencePolicy = confidencePolicy ?? new OcrConfidencePolicy();
    }

    public OcrProviderRoutingDecision Route(OcrProviderRoutingRequest request)
    {
        var confidence = _confidencePolicy.Evaluate(request.ExtractionConfidence);
        var reasons = new List<string>();

        if (request.ExecutionMode == OcrExecutionMode.LiveBlocked)
        {
            return Decision(OcrRoutingDecisionKind.BlockLiveProvider, null, OcrProviderMode.Disabled, request, confidence, ["Live provider execution is blocked in this design-only block."]);
        }

        if (request.RequestsActionAuthority ||
            request.RequestsBrowserControl ||
            request.RequestsDesktopControl ||
            request.ContainsCaptchaLikeChallenge ||
            request.ContainsLoginLikeFlow ||
            request.ContainsPaymentLikeFlow ||
            request.ContainsFiscalSubmissionLikeFlow)
        {
            return Decision(OcrRoutingDecisionKind.BlockDueToActionAuthorityRequest, null, OcrProviderMode.Disabled, request, confidence, ["OCR cannot authorize actions, browser/desktop control, captcha/login/payment/fiscal automation, or capability unlock."]);
        }

        if ((request.PrivacyLevel is OcrPrivacyLevel.Sensitive or OcrPrivacyLevel.Regulated ||
             request.RiskLevel is OcrRiskLevel.Regulated) &&
            !request.RedactionApplied)
        {
            return Decision(OcrRoutingDecisionKind.BlockDueToSensitiveUnredactedInput, null, OcrProviderMode.Disabled, request, confidence, ["Sensitive or regulated input must be redacted and human-reviewed before any cloud candidate recommendation."]);
        }

        if (request.InputKind == OcrInputKind.PdfLocalFile)
            return RoutePdfPreflight(request, confidence);

        if (request.CostMode == OcrCostMode.FreeOnly)
        {
            return Decision(OcrRoutingDecisionKind.UseLocalOcrFixture, "local.onnx_ocr_fixture", OcrProviderMode.FixtureOnly, request, confidence, ["Free-only mode selects local fixture OCR or human review."]);
        }

        if (request.CostMode == OcrCostMode.PaidLiveBlocked)
        {
            return Decision(OcrRoutingDecisionKind.BlockDueToPaidProviderNotEnabled, null, OcrProviderMode.Disabled, request, confidence, ["Paid provider live execution is not enabled."]);
        }

        if (request.TaskType == OcrTaskType.ScreenPerception && request.InputKind == OcrInputKind.ScreenshotCropFixture)
        {
            return Decision(OcrRoutingDecisionKind.UseLocalOcrFixture, "local.onnx_ocr_fixture", OcrProviderMode.FixtureOnly, request, confidence, ["Screen perception remains local-first; paid document OCR is not selected by default."]);
        }

        if (request.TaskType is OcrTaskType.InvoiceExtraction or OcrTaskType.ReceiptExtraction or OcrTaskType.FiscalDocumentExtraction)
        {
            return Decision(OcrRoutingDecisionKind.RecommendMistralDocumentAiCandidate, "cloud.mistral_document_ai", OcrProviderMode.LiveCandidateBlocked, request, confidence, ["Structured document extraction can recommend Mistral Document AI as a disabled paid candidate."]);
        }

        if (request.TaskType is OcrTaskType.TableExtraction or OcrTaskType.DocumentOcr or OcrTaskType.GenericDocumentUnderstanding)
        {
            return Decision(OcrRoutingDecisionKind.RecommendMistralOcr4Candidate, "cloud.mistral_ocr_4", OcrProviderMode.LiveCandidateBlocked, request, confidence, ["Complex OCR/table/document understanding can recommend Mistral OCR 4 as a disabled paid candidate."]);
        }

        reasons.Add("No safe provider candidate matched; human review required.");
        return Decision(OcrRoutingDecisionKind.RequireHumanReview, "human.review", OcrProviderMode.FixtureOnly, request, confidence, reasons);
    }

    private OcrProviderRoutingDecision RoutePdfPreflight(
        OcrProviderRoutingRequest request,
        OcrConfidenceEvaluation confidence)
    {
        var preflight = request.PdfPreflight;
        if (preflight is null || preflight.Status != PdfPreflightStatus.Ready || preflight.Blocker != PdfPreflightBlocker.None)
        {
            return Decision(
                OcrRoutingDecisionKind.BlockDueToPdfPreflightFailure,
                null,
                OcrProviderMode.Disabled,
                request,
                confidence,
                ["Validated local PDF preflight is required before technique selection."]);
        }

        if (preflight.ExecutionAuthorized || preflight.RawContentPersisted || preflight.NetworkUsed || preflight.ActionAuthority)
        {
            return Decision(
                OcrRoutingDecisionKind.BlockDueToPdfPreflightFailure,
                null,
                OcrProviderMode.Disabled,
                request,
                confidence,
                ["PDF preflight contradicted the local no-authority contract."]);
        }

        var reviewedRequest = request with
        {
            ConflictingFieldsDetected = request.ConflictingFieldsDetected || preflight.HumanReviewRequired
        };
        var pageSummary = preflight.PagesNeedingOcr.Count == 0
            ? "no pages require OCR"
            : $"OCR candidate pages: {string.Join(",", preflight.PagesNeedingOcr)}";

        return preflight.RecommendedTechnique switch
        {
            PdfRecommendedTechnique.NativeTextExtraction or PdfRecommendedTechnique.NativeStructuredExtraction =>
                Decision(
                    OcrRoutingDecisionKind.RecommendNativePdfExtractionCandidate,
                    "local.pdf_inspector_preflight",
                    OcrProviderMode.LocalOnly,
                    reviewedRequest,
                    confidence,
                    [$"Local PDF preflight recommends {preflight.RecommendedTechnique}; {pageSummary}; extraction remains a separate unauthorized candidate."]),
            PdfRecommendedTechnique.HybridNativeAndLocalOcr =>
                Decision(
                    OcrRoutingDecisionKind.RecommendHybridPdfExtractionAndOcr,
                    "local.pdf_inspector_preflight",
                    OcrProviderMode.LocalOnly,
                    reviewedRequest,
                    confidence,
                    [$"Local PDF preflight recommends native extraction plus page-level OCR; {pageSummary}; OCR execution remains blocked."]),
            PdfRecommendedTechnique.LocalOcrAllPages =>
                Decision(
                    OcrRoutingDecisionKind.RecommendLocalOcrForPdfPages,
                    "local.pdf_inspector_preflight",
                    OcrProviderMode.LocalOnly,
                    reviewedRequest,
                    confidence,
                    [$"Local PDF preflight recommends local OCR for all pages; {pageSummary}; OCR execution remains blocked."]),
            _ => Decision(
                OcrRoutingDecisionKind.BlockDueToPdfPreflightFailure,
                null,
                OcrProviderMode.Disabled,
                reviewedRequest,
                confidence,
                ["PDF preflight did not produce a safe local technique recommendation."])
        };
    }

    private OcrProviderRoutingDecision Decision(
        OcrRoutingDecisionKind kind,
        string? providerId,
        OcrProviderMode providerMode,
        OcrProviderRoutingRequest request,
        OcrConfidenceEvaluation confidence,
        IReadOnlyList<string> reasons)
    {
        var humanReview = confidence.HumanReviewRequired ||
            request.RiskLevel is OcrRiskLevel.High or OcrRiskLevel.Regulated ||
            request.PrivacyLevel is OcrPrivacyLevel.Sensitive or OcrPrivacyLevel.Regulated ||
            request.ConflictingFieldsDetected ||
            kind is OcrRoutingDecisionKind.RequireHumanReview or
                OcrRoutingDecisionKind.BlockDueToSensitiveUnredactedInput or
                OcrRoutingDecisionKind.BlockDueToActionAuthorityRequest;

        return new OcrProviderRoutingDecision(
            kind,
            providerId,
            providerMode,
            humanReview,
            LiveExecutionBlocked: true,
            PaidExecutionBlocked: providerMode is OcrProviderMode.LiveCandidateBlocked or OcrProviderMode.Disabled,
            NetworkCallAllowed: false,
            ActionAuthority: false,
            reasons.Concat([confidence.Reason]).ToList(),
            confidence);
    }
}

public sealed class OcrEvidenceRedactor
{
    private static readonly Regex SensitiveKeyValuePattern = new(
        @"(?i)\b(password|passwd|pwd|token|api[_ -]?key|secret|authorization|credential|otp|2fa|mfa|cookie|session|card|ssn|payment)\b\s*[:=]\s*[^,\s;]+",
        RegexOptions.Compiled);
    private static readonly Regex EmailPattern = new(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);
    private static readonly Regex JwtPattern = new(@"eyJ[a-zA-Z0-9_-]+\.eyJ[a-zA-Z0-9_-]+\.[a-zA-Z0-9_-]+", RegexOptions.Compiled);
    private static readonly Regex CreditCardPattern = new(@"\b(?:\d{4}[-\s]?){3}\d{4}\b", RegexOptions.Compiled);
    private static readonly Regex SsnPattern = new(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled);
    private static readonly Regex TokenPattern = new(@"\b(sk-[A-Za-z0-9_-]{8,}|ghp_[A-Za-z0-9_]{8,}|Bearer\s+[A-Za-z0-9._-]{12,})\b", RegexOptions.Compiled);

    public (string Value, IReadOnlyList<OcrRedactionCandidate> Candidates) Redact(string text, string blockId, int pageNumber)
    {
        var candidates = new List<OcrRedactionCandidate>();
        var redacted = text;
        redacted = Replace(SensitiveKeyValuePattern, redacted, candidates, "sensitive-key-value", blockId, pageNumber);
        redacted = Replace(EmailPattern, redacted, candidates, "email", blockId, pageNumber);
        redacted = Replace(JwtPattern, redacted, candidates, "jwt", blockId, pageNumber);
        redacted = Replace(CreditCardPattern, redacted, candidates, "payment-card", blockId, pageNumber);
        redacted = Replace(SsnPattern, redacted, candidates, "ssn", blockId, pageNumber);
        redacted = Replace(TokenPattern, redacted, candidates, "token", blockId, pageNumber);
        return (redacted, candidates);
    }

    private static string Replace(Regex regex, string input, ICollection<OcrRedactionCandidate> candidates, string category, string blockId, int pageNumber) =>
        regex.Replace(input, _ =>
        {
            candidates.Add(new OcrRedactionCandidate(category, pageNumber, blockId, BoundingBox: null, Confidence: 0.99));
            return "[REDACTED]";
        });
}

public sealed class OcrEvidencePackBuilder
{
    private readonly OcrEvidenceRedactor _redactor = new();

    public OcrEvidencePack Build(OcrFixtureDocument fixture, OcrProviderRoutingDecision decision)
    {
        var (redacted, candidates) = _redactor.Redact(fixture.SyntheticText, "block-001", 1);
        var block = new OcrEvidenceBlock(
            "block-001",
            BlockTypeFor(fixture.TaskType),
            PageNumber: 1,
            BoundingBox: new OcrBoundingBox(1, 0.1, 0.1, 0.8, 0.2),
            fixture.Confidence,
            redacted,
            RawTextPresent: false,
            decision.HumanReviewRequired,
            ActionAuthority: false);

        return new OcrEvidencePack(
            $"ocr-evidence-{fixture.FixtureId}",
            fixture.FixtureId,
            SourceHash: $"fixture-sha256-{fixture.FixtureId}",
            decision.ProviderId ?? "none",
            decision.ProviderMode,
            [block],
            candidates,
            decision,
            RawScreenshotStored: false,
            RawDocumentStored: false,
            decision.HumanReviewRequired,
            ActionAuthority: false,
            Summary: $"Fixture {fixture.FixtureId}; decision {decision.Decision}; provider {decision.ProviderId ?? "none"}; text {redacted}");
    }

    private static OcrEvidenceBlockType BlockTypeFor(OcrTaskType taskType) =>
        taskType == OcrTaskType.TableExtraction ? OcrEvidenceBlockType.Table :
        taskType is OcrTaskType.InvoiceExtraction or OcrTaskType.ReceiptExtraction or OcrTaskType.FiscalDocumentExtraction ? OcrEvidenceBlockType.KeyValue :
        OcrEvidenceBlockType.Text;
}

public static class OcrDocumentFixtures
{
    public static OcrFixtureDocument SimpleInvoiceFixture() => new(
        "simple_invoice_fixture",
        OcrTaskType.InvoiceExtraction,
        OcrInputKind.PdfFixture,
        OcrRiskLevel.Medium,
        OcrPrivacyLevel.Normal,
        RedactionApplied: true,
        ContainsSensitiveRawText: false,
        ContainsConflictingFields: false,
        ContainsCaptchaLikeChallenge: false,
        ContainsLoginLikeFlow: false,
        ContainsPaymentLikeFlow: false,
        ContainsFiscalSubmissionLikeFlow: false,
        Confidence: 0.93,
        SyntheticText: "Invoice INV-0001 total 123.45 vendor Example Supplies");

    public static OcrFixtureDocument TableFixture() => new(
        "table_fixture",
        OcrTaskType.TableExtraction,
        OcrInputKind.DocumentFixture,
        OcrRiskLevel.Low,
        OcrPrivacyLevel.Normal,
        RedactionApplied: true,
        ContainsSensitiveRawText: false,
        ContainsConflictingFields: false,
        ContainsCaptchaLikeChallenge: false,
        ContainsLoginLikeFlow: false,
        ContainsPaymentLikeFlow: false,
        ContainsFiscalSubmissionLikeFlow: false,
        Confidence: 0.91,
        SyntheticText: "SKU Qty Price A-1 2 10.00 B-2 1 20.00");

    public static OcrFixtureDocument LowConfidenceFixture() => new(
        "low_confidence_fixture",
        OcrTaskType.DocumentOcr,
        OcrInputKind.ImageFixture,
        OcrRiskLevel.Medium,
        OcrPrivacyLevel.Normal,
        RedactionApplied: true,
        ContainsSensitiveRawText: false,
        ContainsConflictingFields: false,
        ContainsCaptchaLikeChallenge: false,
        ContainsLoginLikeFlow: false,
        ContainsPaymentLikeFlow: false,
        ContainsFiscalSubmissionLikeFlow: false,
        Confidence: 0.42,
        SyntheticText: "blurred fixture text");

    public static OcrFixtureDocument SensitiveDocumentFixture() => new(
        "sensitive_document_fixture",
        OcrTaskType.GenericDocumentUnderstanding,
        OcrInputKind.DocumentFixture,
        OcrRiskLevel.Regulated,
        OcrPrivacyLevel.Regulated,
        RedactionApplied: false,
        ContainsSensitiveRawText: true,
        ContainsConflictingFields: false,
        ContainsCaptchaLikeChallenge: false,
        ContainsLoginLikeFlow: false,
        ContainsPaymentLikeFlow: false,
        ContainsFiscalSubmissionLikeFlow: false,
        Confidence: 0.94,
        SyntheticText: "Fake ID document name Example Person ssn=123-45-6789 token=sk-fixture-token-0000");

    public static OcrFixtureDocument ConflictingFieldsFixture() => new(
        "conflicting_fields_fixture",
        OcrTaskType.InvoiceExtraction,
        OcrInputKind.PdfFixture,
        OcrRiskLevel.High,
        OcrPrivacyLevel.Normal,
        RedactionApplied: true,
        ContainsSensitiveRawText: false,
        ContainsConflictingFields: true,
        ContainsCaptchaLikeChallenge: false,
        ContainsLoginLikeFlow: false,
        ContainsPaymentLikeFlow: false,
        ContainsFiscalSubmissionLikeFlow: false,
        Confidence: 0.85,
        SyntheticText: "Invoice total 100.00 and total 180.00 conflict fixture");

    public static OcrFixtureDocument ScreenCropFixture() => new(
        "screen_crop_fixture",
        OcrTaskType.ScreenPerception,
        OcrInputKind.ScreenshotCropFixture,
        OcrRiskLevel.Low,
        OcrPrivacyLevel.Normal,
        RedactionApplied: true,
        ContainsSensitiveRawText: false,
        ContainsConflictingFields: false,
        ContainsCaptchaLikeChallenge: false,
        ContainsLoginLikeFlow: false,
        ContainsPaymentLikeFlow: false,
        ContainsFiscalSubmissionLikeFlow: false,
        Confidence: 0.78,
        SyntheticText: "Button-like fixture crop text");

    public static OcrFixtureDocument CaptchaLikeFixture() => Blocked("captcha_like_fixture", captcha: true);

    public static OcrFixtureDocument LoginLikeFixture() => Blocked("login_like_fixture", login: true);

    public static OcrFixtureDocument PaymentLikeFixture() => Blocked("payment_like_fixture", payment: true);

    public static OcrFixtureDocument FiscalSubmissionLikeFixture() => Blocked("fiscal_submission_like_fixture", fiscal: true);

    private static OcrFixtureDocument Blocked(string id, bool captcha = false, bool login = false, bool payment = false, bool fiscal = false) => new(
        id,
        fiscal ? OcrTaskType.FiscalDocumentExtraction : OcrTaskType.ScreenPerception,
        fiscal ? OcrInputKind.DocumentFixture : OcrInputKind.ScreenshotCropFixture,
        OcrRiskLevel.Regulated,
        OcrPrivacyLevel.Sensitive,
        RedactionApplied: true,
        ContainsSensitiveRawText: login || payment,
        ContainsConflictingFields: false,
        ContainsCaptchaLikeChallenge: captcha,
        ContainsLoginLikeFlow: login,
        ContainsPaymentLikeFlow: payment,
        ContainsFiscalSubmissionLikeFlow: fiscal,
        Confidence: 0.92,
        SyntheticText: "blocked synthetic fixture");
}
