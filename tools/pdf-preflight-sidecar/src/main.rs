use pdf_inspector::{process_pdf_with_options, PdfOptions, PdfType, ProcessMode};
use serde::Serialize;
use std::env;
use std::fs;
use std::path::Path;
use std::process;

const SCHEMA_VERSION: u32 = 1;
const ENGINE_REVISION: &str = "a15ec2d68d51dbe6a39d1da688ec7a3f642d846c";
const DEFAULT_MAX_BYTES: u64 = 100 * 1024 * 1024;

#[derive(Serialize)]
struct PageReasonOutput {
    page: u32,
    reasons: Vec<String>,
}

#[derive(Serialize)]
struct SuccessOutput {
    schema_version: u32,
    status: &'static str,
    engine: &'static str,
    engine_revision: &'static str,
    pdf_type: &'static str,
    page_count: u32,
    confidence: f32,
    pages_needing_ocr: Vec<u32>,
    ocr_reasons_by_page: Vec<PageReasonOutput>,
    is_complex: bool,
    pages_with_tables: Vec<u32>,
    pages_with_columns: Vec<u32>,
    has_encoding_issues: bool,
    processing_time_ms: u64,
    raw_content_persisted: bool,
    network_used: bool,
    action_authority: bool,
}

#[derive(Serialize)]
struct ErrorOutput {
    schema_version: u32,
    status: &'static str,
    engine: &'static str,
    engine_revision: &'static str,
    error_code: &'static str,
    raw_content_persisted: bool,
    network_used: bool,
    action_authority: bool,
}

fn main() {
    let args: Vec<String> = env::args().collect();
    if args.len() != 2 {
        fail("invalid_arguments");
    }

    let path = Path::new(&args[1]);
    let max_bytes = env::var("NODAL_PDF_PREFLIGHT_MAX_BYTES")
        .ok()
        .and_then(|value| value.parse::<u64>().ok())
        .filter(|value| *value > 0)
        .unwrap_or(DEFAULT_MAX_BYTES);

    let metadata = match fs::metadata(path) {
        Ok(metadata) if metadata.is_file() => metadata,
        _ => fail("input_unavailable"),
    };
    if metadata.len() == 0 || metadata.len() > max_bytes {
        fail("input_size_rejected");
    }

    let result = match process_pdf_with_options(path, PdfOptions::new().mode(ProcessMode::Analyze))
    {
        Ok(result) => result,
        Err(_) => fail("inspection_failed"),
    };

    let output = SuccessOutput {
        schema_version: SCHEMA_VERSION,
        status: "ok",
        engine: "firecrawl/pdf-inspector",
        engine_revision: ENGINE_REVISION,
        pdf_type: match result.pdf_type {
            PdfType::TextBased => "text_based",
            PdfType::Scanned => "scanned",
            PdfType::ImageBased => "image_based",
            PdfType::Mixed => "mixed",
        },
        page_count: result.page_count,
        confidence: result.confidence,
        pages_needing_ocr: result.pages_needing_ocr,
        ocr_reasons_by_page: result
            .ocr_reasons_by_page
            .into_iter()
            .map(|entry| PageReasonOutput {
                page: entry.page,
                reasons: entry.reasons,
            })
            .collect(),
        is_complex: result.layout.is_complex,
        pages_with_tables: result.layout.pages_with_tables,
        pages_with_columns: result.layout.pages_with_columns,
        has_encoding_issues: result.has_encoding_issues,
        processing_time_ms: result.processing_time_ms,
        raw_content_persisted: false,
        network_used: false,
        action_authority: false,
    };

    println!(
        "{}",
        serde_json::to_string(&output).expect("serializable output")
    );
}

fn fail(error_code: &'static str) -> ! {
    let output = ErrorOutput {
        schema_version: SCHEMA_VERSION,
        status: "error",
        engine: "firecrawl/pdf-inspector",
        engine_revision: ENGINE_REVISION,
        error_code,
        raw_content_persisted: false,
        network_used: false,
        action_authority: false,
    };
    println!(
        "{}",
        serde_json::to_string(&output).expect("serializable error")
    );
    process::exit(2);
}
