using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OneBrain.DocumentIntelligence;

public enum PdfPreflightStatus
{
    Ready,
    Blocked
}

public enum PdfContentKind
{
    Unknown,
    TextBased,
    Scanned,
    ImageBased,
    Mixed
}

public enum PdfRecommendedTechnique
{
    None,
    NativeTextExtraction,
    NativeStructuredExtraction,
    HybridNativeAndLocalOcr,
    LocalOcrAllPages,
    HumanReview
}

public enum PdfPreflightBlocker
{
    None,
    InputMissing,
    InputNotPdf,
    InputSizeRejected,
    PolicyInvalid,
    SidecarMissing,
    SidecarHashInvalid,
    SidecarTimedOut,
    SidecarOutputRejected,
    SidecarRejectedDocument,
    UnsupportedSchema,
    UnexpectedEngineRevision,
    ContradictoryResult,
    InputChangedDuringInspection
}

public sealed record PdfPageOcrReason(int PageNumber, IReadOnlyList<string> Reasons);

public sealed record PdfInspectorSidecarOptions(
    string ExecutablePath,
    string ExpectedExecutableSha256,
    long MaxFileBytes,
    int MaxPageCount,
    TimeSpan Timeout,
    int MaxOutputCharacters)
{
    public const string PinnedEngineRevision = "a15ec2d68d51dbe6a39d1da688ec7a3f642d846c";

    public static PdfInspectorSidecarOptions Create(
        string executablePath,
        string expectedExecutableSha256,
        long maxFileBytes = 100 * 1024 * 1024,
        int maxPageCount = 500,
        int timeoutSeconds = 10,
        int maxOutputCharacters = 256 * 1024) =>
        new(
            executablePath,
            expectedExecutableSha256,
            maxFileBytes,
            maxPageCount,
            TimeSpan.FromSeconds(timeoutSeconds),
            maxOutputCharacters);
}

public sealed record PdfPreflightResult(
    PdfPreflightStatus Status,
    PdfContentKind ContentKind,
    PdfRecommendedTechnique RecommendedTechnique,
    int PageCount,
    double Confidence,
    IReadOnlyList<int> PagesNeedingOcr,
    IReadOnlyList<PdfPageOcrReason> OcrReasonsByPage,
    bool IsComplex,
    IReadOnlyList<int> PagesWithTables,
    IReadOnlyList<int> PagesWithColumns,
    bool HasEncodingIssues,
    bool HumanReviewRequired,
    bool ExecutionAuthorized,
    bool RawContentPersisted,
    bool NetworkUsed,
    bool ActionAuthority,
    string? SourceSha256,
    PdfPreflightBlocker Blocker,
    string Summary);

public sealed record PdfInspectorProcessResult(
    int ExitCode,
    string StandardOutput,
    bool TimedOut,
    bool OutputLimitExceeded);

public interface IPdfInspectorProcessRunner
{
    Task<PdfInspectorProcessResult> RunAsync(
        string executablePath,
        string pdfPath,
        long maxFileBytes,
        TimeSpan timeout,
        int maxOutputCharacters,
        CancellationToken cancellationToken);
}

public sealed class PdfInspectorProcessRunner : IPdfInspectorProcessRunner
{
    public async Task<PdfInspectorProcessResult> RunAsync(
        string executablePath,
        string pdfPath,
        long maxFileBytes,
        TimeSpan timeout,
        int maxOutputCharacters,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!
        };
        startInfo.ArgumentList.Add(pdfPath);
        startInfo.Environment["NODAL_PDF_PREFLIGHT_MAX_BYTES"] = maxFileBytes.ToString(System.Globalization.CultureInfo.InvariantCulture);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            return new PdfInspectorProcessResult(-1, string.Empty, TimedOut: false, OutputLimitExceeded: false);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
                throw;

            timedOut = true;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        var outputLimitExceeded = stdout.Length > maxOutputCharacters || stderr.Length > maxOutputCharacters;
        if (stdout.Length > maxOutputCharacters)
            stdout = string.Empty;

        return new PdfInspectorProcessResult(process.ExitCode, stdout, timedOut, outputLimitExceeded);
    }
}

public sealed class PdfPreflightService
{
    private static readonly HashSet<string> AllowedOcrReasons = new(StringComparer.Ordinal)
    {
        "scanned",
        "no_text",
        "vector_text",
        "suspected_garbled_text"
    };

    private readonly IPdfInspectorProcessRunner processRunner;

    public PdfPreflightService(IPdfInspectorProcessRunner? processRunner = null)
    {
        this.processRunner = processRunner ?? new PdfInspectorProcessRunner();
    }

    public async Task<PdfPreflightResult> InspectAsync(
        string pdfPath,
        PdfInspectorSidecarOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options.MaxFileBytes <= 0 ||
            options.MaxPageCount <= 0 ||
            options.Timeout <= TimeSpan.Zero ||
            options.MaxOutputCharacters <= 0)
        {
            return Block(PdfPreflightBlocker.PolicyInvalid, "PDF preflight policy is invalid.");
        }

        if (string.IsNullOrWhiteSpace(pdfPath))
            return Block(PdfPreflightBlocker.InputMissing, "PDF input is required.");

        var fullPdfPath = Path.GetFullPath(pdfPath);
        if (!File.Exists(fullPdfPath))
            return Block(PdfPreflightBlocker.InputMissing, "PDF input is unavailable.");

        var inputInfo = new FileInfo(fullPdfPath);
        if (!string.Equals(inputInfo.Extension, ".pdf", StringComparison.OrdinalIgnoreCase) || !await HasPdfSignatureAsync(fullPdfPath, cancellationToken).ConfigureAwait(false))
            return Block(PdfPreflightBlocker.InputNotPdf, "Input is not a validated PDF.");
        if (inputInfo.Length == 0 || inputInfo.Length > options.MaxFileBytes)
            return Block(PdfPreflightBlocker.InputSizeRejected, "PDF size is outside the local preflight policy.");

        var fullExecutablePath = Path.GetFullPath(options.ExecutablePath);
        if (!File.Exists(fullExecutablePath))
            return Block(PdfPreflightBlocker.SidecarMissing, "Pinned PDF inspector sidecar is unavailable.");
        if (!IsSha256(options.ExpectedExecutableSha256))
            return Block(PdfPreflightBlocker.SidecarHashInvalid, "PDF inspector hash policy is invalid.");

        var executableHash = await ComputeSha256Async(fullExecutablePath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(executableHash, options.ExpectedExecutableSha256, StringComparison.OrdinalIgnoreCase))
            return Block(PdfPreflightBlocker.SidecarHashInvalid, "PDF inspector binary hash does not match policy.");

        var sourceHashBefore = await ComputeSha256Async(fullPdfPath, cancellationToken).ConfigureAwait(false);
        var processResult = await processRunner.RunAsync(
            fullExecutablePath,
            fullPdfPath,
            options.MaxFileBytes,
            options.Timeout,
            options.MaxOutputCharacters,
            cancellationToken).ConfigureAwait(false);

        if (processResult.TimedOut)
            return Block(PdfPreflightBlocker.SidecarTimedOut, "PDF inspection timed out.", sourceHashBefore);
        if (processResult.OutputLimitExceeded || string.IsNullOrWhiteSpace(processResult.StandardOutput))
            return Block(PdfPreflightBlocker.SidecarOutputRejected, "PDF inspector output was rejected.", sourceHashBefore);

        var sourceHashAfter = await ComputeSha256Async(fullPdfPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(sourceHashBefore, sourceHashAfter, StringComparison.Ordinal))
            return Block(PdfPreflightBlocker.InputChangedDuringInspection, "PDF changed during inspection.", sourceHashBefore);

        if (processResult.ExitCode != 0)
            return Block(PdfPreflightBlocker.SidecarRejectedDocument, "PDF inspector rejected the document.", sourceHashBefore);

        return Parse(processResult.StandardOutput, options.MaxPageCount, sourceHashBefore);
    }

    public static PdfPreflightResult Parse(string json, int maxPageCount = 500, string? sourceSha256 = null)
    {
        InspectorOutput? output;
        try
        {
            output = JsonSerializer.Deserialize<InspectorOutput>(json);
        }
        catch (JsonException)
        {
            return Block(PdfPreflightBlocker.SidecarOutputRejected, "PDF inspector JSON was rejected.", sourceSha256);
        }

        if (output is null || !string.Equals(output.Status, "ok", StringComparison.Ordinal))
            return Block(PdfPreflightBlocker.SidecarRejectedDocument, "PDF inspector did not return a successful result.", sourceSha256);
        if (output.SchemaVersion != 1)
            return Block(PdfPreflightBlocker.UnsupportedSchema, "PDF inspector schema is unsupported.", sourceSha256);
        if (!string.Equals(output.EngineRevision, PdfInspectorSidecarOptions.PinnedEngineRevision, StringComparison.Ordinal))
            return Block(PdfPreflightBlocker.UnexpectedEngineRevision, "PDF inspector revision does not match policy.", sourceSha256);
        if (output.RawContentPersisted || output.NetworkUsed || output.ActionAuthority)
            return Block(PdfPreflightBlocker.ContradictoryResult, "PDF inspector violated the local no-authority contract.", sourceSha256);
        if (output.PageCount <= 0 || output.PageCount > maxPageCount || output.Confidence is < 0 or > 1)
            return Block(PdfPreflightBlocker.ContradictoryResult, "PDF inspector returned invalid bounds.", sourceSha256);

        var contentKind = output.PdfType switch
        {
            "text_based" => PdfContentKind.TextBased,
            "scanned" => PdfContentKind.Scanned,
            "image_based" => PdfContentKind.ImageBased,
            "mixed" => PdfContentKind.Mixed,
            _ => PdfContentKind.Unknown
        };
        if (contentKind == PdfContentKind.Unknown)
            return Block(PdfPreflightBlocker.ContradictoryResult, "PDF inspector returned an unknown content kind.", sourceSha256);

        var ocrPages = NormalizePages(output.PagesNeedingOcr, output.PageCount);
        var tablePages = NormalizePages(output.PagesWithTables, output.PageCount);
        var columnPages = NormalizePages(output.PagesWithColumns, output.PageCount);
        if (ocrPages is null || tablePages is null || columnPages is null)
            return Block(PdfPreflightBlocker.ContradictoryResult, "PDF inspector returned an invalid page reference.", sourceSha256);

        var reasons = new List<PdfPageOcrReason>();
        foreach (var entry in output.OcrReasonsByPage ?? [])
        {
            var entryReasons = entry.Reasons?.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() ?? [];
            if (!ocrPages.Contains(entry.Page) || entryReasons.Length == 0 || entryReasons.Any(reason => !AllowedOcrReasons.Contains(reason)))
                return Block(PdfPreflightBlocker.ContradictoryResult, "PDF inspector returned invalid OCR reasons.", sourceSha256);
            reasons.Add(new PdfPageOcrReason(entry.Page, entryReasons));
        }
        if (reasons.Select(reason => reason.PageNumber).Distinct().Count() != reasons.Count ||
            ocrPages.Any(page => reasons.All(reason => reason.PageNumber != page)))
            return Block(PdfPreflightBlocker.ContradictoryResult, "PDF inspector OCR pages and reasons disagree.", sourceSha256);

        if ((contentKind is PdfContentKind.Scanned or PdfContentKind.ImageBased) && ocrPages.Count != output.PageCount)
            return Block(PdfPreflightBlocker.ContradictoryResult, "Image-only PDF did not route every page to OCR.", sourceSha256);

        var technique = contentKind switch
        {
            PdfContentKind.TextBased when ocrPages.Count > 0 => PdfRecommendedTechnique.HybridNativeAndLocalOcr,
            PdfContentKind.TextBased when output.IsComplex => PdfRecommendedTechnique.NativeStructuredExtraction,
            PdfContentKind.TextBased => PdfRecommendedTechnique.NativeTextExtraction,
            PdfContentKind.Mixed => PdfRecommendedTechnique.HybridNativeAndLocalOcr,
            PdfContentKind.Scanned or PdfContentKind.ImageBased => PdfRecommendedTechnique.LocalOcrAllPages,
            _ => PdfRecommendedTechnique.HumanReview
        };
        var humanReview = output.Confidence < 0.90 || output.HasEncodingIssues || contentKind != PdfContentKind.TextBased;

        return new PdfPreflightResult(
            PdfPreflightStatus.Ready,
            contentKind,
            technique,
            output.PageCount,
            output.Confidence,
            ocrPages,
            reasons.OrderBy(reason => reason.PageNumber).ToArray(),
            output.IsComplex,
            tablePages,
            columnPages,
            output.HasEncodingIssues,
            humanReview,
            ExecutionAuthorized: false,
            RawContentPersisted: false,
            NetworkUsed: false,
            ActionAuthority: false,
            sourceSha256,
            PdfPreflightBlocker.None,
            $"Local PDF preflight recommends {technique}; execution remains unauthorized.");
    }

    private static IReadOnlyList<int>? NormalizePages(IReadOnlyList<int>? pages, int pageCount)
    {
        var normalized = (pages ?? []).Distinct().Order().ToArray();
        return normalized.Any(page => page < 1 || page > pageCount) ? null : normalized;
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static async Task<bool> HasPdfSignatureAsync(string path, CancellationToken cancellationToken)
    {
        var signature = new byte[5];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var read = await stream.ReadAsync(signature, cancellationToken).ConfigureAwait(false);
        return read == signature.Length && signature.AsSpan().SequenceEqual("%PDF-"u8);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static PdfPreflightResult Block(PdfPreflightBlocker blocker, string summary, string? sourceSha256 = null) =>
        new(
            PdfPreflightStatus.Blocked,
            PdfContentKind.Unknown,
            PdfRecommendedTechnique.None,
            0,
            0,
            [],
            [],
            IsComplex: false,
            [],
            [],
            HasEncodingIssues: false,
            HumanReviewRequired: true,
            ExecutionAuthorized: false,
            RawContentPersisted: false,
            NetworkUsed: false,
            ActionAuthority: false,
            sourceSha256,
            blocker,
            summary);

    private sealed record InspectorOutput(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("engine_revision")] string? EngineRevision,
        [property: JsonPropertyName("pdf_type")] string? PdfType,
        [property: JsonPropertyName("page_count")] int PageCount,
        [property: JsonPropertyName("confidence")] double Confidence,
        [property: JsonPropertyName("pages_needing_ocr")] IReadOnlyList<int>? PagesNeedingOcr,
        [property: JsonPropertyName("ocr_reasons_by_page")] IReadOnlyList<InspectorPageReason>? OcrReasonsByPage,
        [property: JsonPropertyName("is_complex")] bool IsComplex,
        [property: JsonPropertyName("pages_with_tables")] IReadOnlyList<int>? PagesWithTables,
        [property: JsonPropertyName("pages_with_columns")] IReadOnlyList<int>? PagesWithColumns,
        [property: JsonPropertyName("has_encoding_issues")] bool HasEncodingIssues,
        [property: JsonPropertyName("raw_content_persisted")] bool RawContentPersisted,
        [property: JsonPropertyName("network_used")] bool NetworkUsed,
        [property: JsonPropertyName("action_authority")] bool ActionAuthority);

    private sealed record InspectorPageReason(
        [property: JsonPropertyName("page")] int Page,
        [property: JsonPropertyName("reasons")] IReadOnlyList<string>? Reasons);
}
