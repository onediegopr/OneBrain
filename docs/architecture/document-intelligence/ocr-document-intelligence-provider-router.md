# OCR / Document Intelligence Provider Router

Decision target: `GO_DESIGN_ONLY_PROVIDER_ROUTER_READY`

Mode: design-only, fixture-safe, no live provider execution, no billing, no browser or desktop automation.

## Decision

NODAL OS separates two different perception domains:

1. Screen OCR / visual perception fallback.
   - Used for UI crops, screenshots, browser/desktop perception, and visual-only diagnostics.
   - Lower trust than DOM/CDP, UIA, or structured app state.
   - Fixture-only in this block.
   - Never action-authoritative.

2. Document OCR / Document Intelligence.
   - Used for PDFs, invoices, receipts, forms, fiscal documents, scanned documents, tables, remitos, statements, and backoffice documents.
   - May route to deterministic parsers, local OCR, paid document intelligence providers, or human review in future gated work.
   - Fixture-only/design-only in this block.
   - Never action-authoritative.

Mistral OCR 4 and Mistral Document AI are introduced as strong paid provider candidates for document intelligence. They are not a replacement for local-first OCR, not a browser automation engine, and not enabled for live calls.

## Provider Order

Screen/browser/desktop perception order:

1. DOM/CDP signals if explicitly allowed and fixture-safe.
2. Accessibility/UIA signals if explicitly allowed and fixture-safe.
3. Structured app/window state.
4. Local visual/OCR fallback on fixtures only.
5. Paid OCR/VLM fallback as future provider candidate only.
6. Human review.

Document intelligence order:

1. Deterministic parser when possible.
2. Local OCR provider candidate.
3. Mistral OCR 4 provider candidate.
4. Mistral Document AI provider candidate.
5. Other paid provider candidates such as Azure Document Intelligence, Google Document AI, and AWS Textract, design-only.
6. Human review.

## Contracts

The isolated project `OneBrain.DocumentIntelligence` defines:

- Provider descriptors with provider id, provider kind, provider mode, supported inputs/outputs, capabilities, forbidden capabilities and policies.
- Routing requests with task type, risk level, privacy level, cost mode, execution mode, input kind, redaction state and blocked automation flags.
- Routing decisions that never execute a provider.
- Confidence policy that can be changed without changing provider code.
- Evidence packs that store fixture id, source hash, provider decision, redacted blocks, confidence metadata and human review requirements.

Provider modes:

- `Disabled`
- `FixtureOnly`
- `DesignOnly`
- `LocalOnly`
- `LiveCandidateBlocked`

Mistral entries:

- `cloud.mistral_ocr_4`: paid OCR candidate, live candidate blocked.
- `cloud.mistral_document_ai`: document AI candidate, live candidate blocked.

Both have:

- no API key requirement in this block,
- no live client,
- no network calls,
- no billing integration,
- no action authority.

## Router Rules

The router returns a decision only. It never calls a provider.

It explicitly blocks:

- OCR as action authority,
- live provider execution,
- paid provider execution,
- sensitive unredacted input to cloud provider,
- browser control,
- desktop control,
- captcha/login/2FA/challenge automation,
- payment execution,
- fiscal submission automation,
- paid beta or public release unlock.

Allowed decision examples:

- `UseLocalOcrFixture`
- `RecommendMistralOcr4Candidate`
- `RecommendMistralDocumentAiCandidate`
- `RequireHumanReview`
- `BlockLiveProvider`
- `BlockDueToSensitiveUnredactedInput`
- `BlockDueToActionAuthorityRequest`
- `BlockDueToNetworkNotAllowed`
- `BlockDueToPaidProviderNotEnabled`

## Confidence Policy

Default thresholds:

- `>= 0.90`: high confidence observation.
- `0.70 - 0.89`: medium confidence, review recommended.
- `< 0.70`: low confidence, review required.
- missing confidence: review required.

Confidence can only influence observation acceptance, redaction requirements, human review requirements and safe provider recommendation. It cannot authorize actions.

## Local PDF Preflight

`local.pdf_inspector_preflight` is a deterministic, local-only metadata provider backed by the pinned `firecrawl/pdf-inspector` revision `a15ec2d68d51dbe6a39d1da688ec7a3f642d846c`. Before any PDF extraction candidate is selected, it classifies the document as text, mixed, scanned or image-based; reports layout/encoding signals; and identifies the exact pages that may require OCR.

The .NET boundary verifies the input signature and size, sidecar SHA-256, timeout, output limit, schema/revision, page bounds and OCR reason allowlist. It then recommends one of native text extraction, structured native extraction, hybrid page-level OCR or all-page local OCR. A recommendation never executes OCR or extraction, persists raw content, uses network access or grants action authority. Missing, malformed, contradictory or unpinned evidence blocks routing.

## Evidence And Redaction

Evidence defaults:

- raw text is not stored by default for sensitive docs,
- raw screenshots are not stored,
- raw documents are not stored,
- redacted text is stored,
- redaction candidates are preserved,
- bounding box redaction candidates are preserved,
- fixture id and source hash are preserved,
- provider id and mode are preserved,
- human review flag is preserved,
- `ActionAuthority` is always false.

Forbidden evidence:

- raw user screenshot,
- raw fiscal document,
- raw ID document,
- raw credentials,
- raw API keys,
- raw payment data,
- raw secrets.

## Future Gates Before Live Mistral OCR

Future live Mistral OCR requires all of the following before implementation:

- explicit user BYOK/API-key settings,
- provider billing disclosure,
- per-task consent,
- privacy classification,
- redaction preflight,
- data retention policy,
- evidence policy,
- audit log,
- live network allowlist,
- provider timeout/retry policy,
- cost cap,
- human review gate,
- provider error handling,
- no action-authority policy,
- manual QA with synthetic documents,
- security review.

## NO-GO Preserved

- `LIVE_MISTRAL_OCR_CALLS: NO-GO`
- `PAID_PROVIDER_EXECUTION: NO-GO`
- `OCR_ACTION_AUTHORITY: NO-GO`
- `BROWSER_LIVE_AUTOMATION: NO-GO`
- `DESKTOP_LIVE_AUTOMATION: NO-GO`
- `CAPTCHA_LOGIN_2FA_PAYMENT_FISCAL_AUTOMATION: NO-GO`
- `PUBLIC_RELEASE_UNLOCK: NO-GO`
- `PAID_BETA_UNLOCK: NO-GO`
