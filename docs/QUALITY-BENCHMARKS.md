# Quality Benchmarks

Which public benchmarks measure FileFlux's own responsibility — turning a document into faithful Markdown (text, reading
order, headings, tables) — and which formats no benchmark covers.

## Native PDF text: olmOCR-Bench and READoc

Both ship real PDFs, so they exercise the native text-layer path rather than OCR.

| Benchmark | Data | Metric |
|---|---|---|
| [olmOCR-Bench](https://huggingface.co/datasets/allenai/olmOCR-bench) ([paper](https://arxiv.org/abs/2502.18443), ODC-BY) | 1,403 single-page PDFs, 7,010 pass/fail checks, English | pass rate on text presence/absence, reading order, table-neighbour and math checks |
| [READoc](https://huggingface.co/datasets/lazyc/READoc) ([paper](https://arxiv.org/abs/2409.05137), MIT) | 3,576 multi-page PDFs (arXiv, GitHub, Zenodo; 27 languages in the Zenodo part) | edit distance per element (text, headings), table TEDS, reading order |

## Tables with merged cells: OmniDocBench tables

[OmniDocBench](https://github.com/opendatalab/OmniDocBench) (CVPR 2025) scores tables with TEDS / TEDS-S and marks merged
cells (`with_span`). Its pages are images, so it measures the OCR path; the subset that matters here is its tables with
`with_span = true`. Its data is for research use only, and its whole-page scores are near saturation at the top of its
leaderboard.

## Where no established benchmark exists

DOCX, PPTX, XLSX, HWP/HWPX and HTML conversion, chunking quality on its own, and Korean document parsing: the only
comparisons we found are vendor-run. These stay measured by this repository's extraction goldens.

Checked again in 2026-10 for Korean and HWP/HWPX:

| Candidate | Verdict | Why |
|---|---|---|
| [opendataloader-bench](https://github.com/opendataloader-project/opendataloader-bench) (Apache-2.0 code, MIT data from DP-Bench, 200 PDFs) | Not adopted for now | All 200 documents are English; reading order and tables are already covered by olmOCR-Bench, READoc and OmniDocBench. Its heading metric (MHS) is the one new axis. |
| Korean RAG and document sets on Hugging Face (RAG answering, key-field extraction, synthetic OCR images) | Not adopted | None annotates parsed structure or text; they measure answering, field extraction or OCR training. |
| AI-Hub Korean document datasets | Not adopted | Access needs identity verification and approval, and redistribution is forbidden — public tooling cannot fetch them. |
| HWP/HWPX with ground truth | None exists | |

The direction for Korean and HWP/HWPX is a measure that needs no hand-made gold: public documents released under the
Korea Open Government License type 1 often ship the same text as HWPX and PDF, so parsing both and comparing the
extracted text (character-level similarity, with tables compared cell by cell) shows what one reader loses that the other
keeps.

## Status

Not yet measured. Results will be added here with the package version, the parser versions and whether an OCR or vision
service was configured.
