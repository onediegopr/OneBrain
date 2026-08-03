# NODAL PDF preflight sidecar

Local-only, no-authority adapter over `firecrawl/pdf-inspector`. It inspects a PDF before OCR routing and emits one bounded JSON object. It does not extract or persist document text, call a network service, authorize actions, or execute OCR.

The dependency is pinned to commit `a15ec2d68d51dbe6a39d1da688ec7a3f642d846c`. Cargo verifies and locks the exact transitive dependency graph in `Cargo.lock`.

Build:

```powershell
cargo build --manifest-path tools/pdf-preflight-sidecar/Cargo.toml --release --locked
```

Record the binary policy hash after every rebuild:

```powershell
Get-FileHash tools/pdf-preflight-sidecar/target/release/nodal-pdf-preflight.exe -Algorithm SHA256
```

Run:

```powershell
tools/pdf-preflight-sidecar/target/release/nodal-pdf-preflight.exe C:\path\document.pdf
```

The .NET adapter independently enforces file size, PDF signature, executable hash, timeout, output size, schema version, engine revision, page bounds, reason allowlists, and fail-closed routing.

This sidecar is a local build input and is not included in the product package. Packaging or redistribution requires a separate transitive-license review and an explicit runtime/product gate.
