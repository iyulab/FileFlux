# FileFlux CLI

Command-line interface for FileFlux document processing SDK. Extract, chunk, and process documents for RAG systems with AI-powered metadata enrichment.

## 설치

### NuGet 패키지로 설치 (권장)

```bash
dotnet tool install --global FileFlux.CLI
```

### 소스에서 빌드

```bash
cd cli/FileFlux.CLI
dotnet build
dotnet run -- --help
```

## 명령어

### `info` - 문서 정보 표시

문서의 기본 정보와 메타데이터를 표시합니다.

```bash
fileflux info document.pdf
```

**기능**:
- 파일 정보 (크기, 수정일, 확장자)
- 지원 형식 확인
- 콘텐츠 분석 (문자수, 단어수, 토큰 추정치)
- 환경 설정 상태

### `extract` - 원본 텍스트 추출

문서에서 원본 텍스트와 콘텐츠를 추출합니다.

```bash
fileflux extract document.pdf -o out
fileflux extract document.docx -f json
fileflux extract document.pdf -q
```

**옵션**:
- `-o, --output <dir>` - 출력 디렉터리 (기본값: 입력 파일 옆 `<입력 파일명>_output/`, 아래 «출력 형식»)
- `-f, --format <format>` - 출력 형식: md, json (기본값: md)
- `-a, --ai` - AI 이미지 분석 사용 (AI 공급자 필요)
- `-q, --quiet` - 최소 출력

**특징**:
- AI 불필요 (기본 처리만)
- 대용량 청크 (100,000자)로 추출
- 메타데이터 포함

### `chunk` - 지능형 청킹

문서를 지능적으로 청크로 분할합니다.

```bash
fileflux chunk document.pdf -s Paragraph -m 512
fileflux chunk document.pdf --ai --enrich --strategy Auto
fileflux chunk document.docx -m 1024 -l 128
```

**옵션**:
- `-o, --output <dir>` - 출력 디렉터리 (기본값: 입력 파일 옆 `<입력 파일명>_output/`, 아래 «출력 형식»)
- `-f, --format <format>` - 출력 형식: md, json, jsonl (기본값: md)
- `-s, --strategy <strategy>` - 청킹 전략: Auto, Sentence, Paragraph, Token, Semantic, Hierarchical (기본값: Auto)
- `-m, --max-size <size>` - 최대 청크 크기 (토큰 단위, 기본값: 512)
- `-l, --overlap <size>` - 청크 간 중복 크기 (토큰 단위, 기본값: 64)
- `-r, --refine` - 청킹 전에 정제 단계 실행 (머리글·바닥글·공백 정리, 구조 재정렬)
- `-a, --ai` - AI 기능 사용 (청크 강화, 이미지 분석). AI 공급자 필요
- `-e, --enrich` - 청킹 뒤 AI 강화 단계 실행 (요약·키워드). **`--ai` 와 함께 줘야 한다** — `--ai` 없이 주면 입력을 읽기 전에 오류(종료 코드 1)로 끝난다
- `--no-extract-images` · `--min-image-size <bytes>` · `--min-image-dimension <px>` - 이미지 추출 설정
- `-q, --quiet` - 최소 출력 · `-v, --verbose` - 상세 출력

**청킹 전략** (라이브러리의 `ChunkingStrategies`, 대소문자 무관 — 그 밖의 이름은 오류):
- `Auto` - 텍스트를 보고 Sentence · Paragraph · Token 중 하나를 고른다
- `Sentence` - 문장 경계 단위
- `Paragraph` - 단락 단위 (Markdown · 블로그 글)
- `Token` - 토큰 예산 단위 (구조 없는 텍스트)
- `Hierarchical` - 제목 구조를 따른다
- `Semantic` - 임베딩 유사도 경계 (임베더 필요)

### `process` - 완전한 파이프라인

추출, 청킹, 강화를 포함한 완전한 처리 파이프라인입니다.

```bash
fileflux process document.pdf -o out
fileflux process document.pdf --no-enrich
fileflux process document.pdf --no-ai --no-refine
fileflux process document.docx -s Auto -m 512 -l 64
```

**옵션**:
- `-o, --output <dir>` - 출력 디렉터리 (기본값: 입력 파일 옆 `<입력 파일명>_output/`, 아래 «출력 형식»)
- `-f, --format <format>` - 출력 형식: md, json, jsonl (기본값: md)
- `-s, --strategy <strategy>` - 청킹 전략 (`chunk` 와 같음, 기본값: Auto)
- `-m, --max-size <size>` - 최대 청크 크기 (기본값: 512)
- `-l, --overlap <size>` - 중복 크기 (기본값: 64)
- `--no-refine` - 정제 단계 건너뛰기
- `--no-enrich` - AI 강화 단계 건너뛰기
- `--no-ai` - AI 기능 끄기 (강화 · 이미지 분석). 이것을 주지 않으면 AI 가 켜진다
- `--no-extract-images` · `--min-image-size <bytes>` · `--min-image-dimension <px>` - 이미지 추출 설정
- `-q, --quiet` - 최소 출력 · `-v, --verbose` - 상세 출력

**기본 동작**:
- 정제와 AI 강화가 켜져 있다 — `chunk` 와 반대로 끄는 플래그(`--no-refine` · `--no-enrich` · `--no-ai`)를 쓴다
- AI 공급자가 설정되지 않았으면 LMSupply 로컬 모델을 쓴다 (아래 «AI 공급자 설정»)
- 진행 상황 표시 및 상세 요약
- 생성된 청크에 대한 품질 메트릭

## AI 공급자 설정

FileFlux CLI는 선택적 AI 메타데이터 강화를 위해 환경 변수를 사용합니다.

### OpenAI 설정

```bash
# Windows (PowerShell)
$env:OPENAI_API_KEY = "sk-..."
$env:OPENAI_MODEL = "gpt-5-nano"  # 선택사항, 기본값: gpt-5-nano

# Linux/Mac
export OPENAI_API_KEY="sk-..."
export OPENAI_MODEL="gpt-5-nano"  # 선택사항
```

### Anthropic 설정

```bash
export ANTHROPIC_API_KEY="sk-ant-..."
export ANTHROPIC_MODEL="claude-haiku-5-5"  # 선택사항, 기본값: claude-haiku-5-5
```

### Google Gemini 설정

```bash
export GOOGLE_API_KEY="..."            # 또는 GEMINI_API_KEY
export GOOGLE_MODEL="gemini-flash-latest"  # 선택사항, 기본값: gemini-flash-latest
```

### 설정 값을 찾는 순서와 공급자 선택

- 각 값은 환경 변수를 먼저 보고, 없으면 `fileflux set <key> <value>` 로 저장한 설정 파일을 본다. `fileflux get` 은 저장된 값과 쓸 수 있는 키를 보여 준다.
- 공급자는 `MODEL_PROVIDER`(`openai` · `anthropic` · `google` · `gpustack` · `local`)로 지정한다. 지정하지 않으면 API 키가 하나뿐일 때 그 공급자를 쓰고, 둘 이상이면 `MODEL_PROVIDER` 를 요구하며, 하나도 없으면 LMSupply 로컬 모델을 쓴다(`LMSUPPLY_AUTO_FALLBACK=false` 로 끈다).

## 출력 형식

`extract`·`chunk`·`process` 는 파일 하나가 아니라 디렉터리를 쓴다. `-o` 를 주지 않으면 입력 파일 옆의 `<입력 파일명>_output/`,
`-o <dir>` 를 주면 `<dir>/<입력 파일명>_output/` 이다. `chunk` 의 결과는 이렇다(`-f` 기본값 `md`):

```
document.pdf_output/
├── extract/extracted.md   # 추출한 원문
├── images/                # 추출한 이미지 (--no-extract-images 이면 없음)
└── chunks/
    ├── content.md         # 청킹한 문서 전체 (-f json 이면 content.json)
    ├── 001.md             # 청크 본문 (-f md)
    ├── 001.json           # 청크 정보 (-f md · json)
    ├── chunks.jsonl       # 모든 청크, 한 줄에 하나 (-f jsonl 일 때만)
    └── info.json          # 실행 옵션 · 통계 · 문서 요약
```

`--refine` · `--enrich` 를 켜면 `refine/` · `enrich/` 디렉터리가 더해진다. 명령이 쓰지 않는 `-f` 값(예: `chunk -f markdown`,
`extract -f jsonl`)은 아무것도 쓰기 전에 허용 값을 알려 주며 종료 코드 1 로 끝난다.

청크의 `start_char` · `end_char` 는 청킹한 텍스트 — `extract/extracted.md` (`--refine` 이면 `refine/refined.md`) — 안의 위치다.
그 텍스트에는 리더가 넣은 구조 표시 줄(`<!-- HEADING_START:H1 -->` 등)이 남아 있고 청크 본문에서는 빠지므로, `end_char - start_char`
가 청크 길이보다 클 수 있다. 그 범위를 잘라 표시 줄을 지우면 청크 본문이 된다.

### `md` (기본값)

청크마다 본문 파일 `NNN.md` 와 정보 파일 `NNN.json` 을 쓴다.

```markdown
---
chunk_index: 1
chunk_id: "59efc50a-542b-4aa1-bd5c-d6fcc1e361d4"
start_char: 0
end_char: 288
---

# Solar Guide

Solar panels convert light into electricity. ...
```

```json
{
  "index": 1,
  "id": "59efc50a-542b-4aa1-bd5c-d6fcc1e361d4",
  "length": 184,
  "tokens": 72,
  "location": {
    "startChar": 0,
    "endChar": 288,
    "headingPath": ["Solar Guide"]
  },
  "quality": {
    "overall": 1,
    "density": 1,
    "importance": 1
  },
  "enrichment": {
    "topic": "solar",
    "hierarchyPath": "Solar Guide"
  }
}
```

값이 없는 항목은 빠진다. `--enrich` 로 AI 강화를 켜면 `enrichment` 에 `summary` · `keywords` 가 더해진다.

### `json`

청크마다 `NNN.json` 하나에 본문과 메타데이터를 함께 쓴다. `properties` 는 청크의 `Props` 그대로이며, 키는
`ChunkPropsKeys` 의 이름(`hierarchy.*` · `document.*` · `enriched.*`)이다.

```jsonc
{
  "index": 1,
  "id": "973bb6c3-af79-4d20-9d43-1d81b0120883",
  "content": "# Solar Guide\n\nSolar panels convert light into electricity. ...",
  "location": { "startChar": 0, "endChar": 288 },
  "metadata": { "fileName": "solar.md", "fileSize": 185, /* 문서의 DocumentMetadata: fileType, title, language, pageCount, processedAt, ... */ },
  "properties": {
    "hierarchy.headingLevel": 1,
    "hierarchy.path": "Solar Guide",
    "document.topic": "solar",
    "document.keywords": ["solar", "panels", "batteries", "guide", "convert", "..."]
  }
}
```

`--enrich` 로 AI 강화를 켜면 `properties` 에 `enriched.summary` · `enriched.keywords` 가 더해진다.

### `jsonl`

모든 청크를 `chunks/chunks.jsonl` 한 파일에 한 줄씩 쓴다. 각 줄은 `index` · `id` · `content` · `location` · `metadata`
이며 `properties` 는 없다.

```jsonl
{"index":1,"id":"699e7d7e-...","content":"# Solar Guide\n\n...","location":{"startChar":0,"endChar":288},"metadata":{...}}
```

### `info.json`

```jsonc
{
  "command": "chunk",
  "input": "solar.md",
  "format": "json",
  "document": { "summary": "...", "topics": ["solar"], "keywords": ["solar", "panels", "batteries", "..."] },
  "chunkingOptions": { "strategy": "Auto", "maxChunkSize": 512, "overlapSize": 64 },
  "statistics": {
    "chunkCount": 1, "totalCharacters": 184, "averageChunkSize": 184, "minChunkSize": 184, "maxChunkSize": 184,
    "varianceRatio": 1, "isBalanced": true, "pageCount": 1, "language": "en",
    "imagesExtracted": 0, "imagesSkipped": 0, "enrichedChunks": 0, "skippedEnrichments": 0
  },
  "aiAnalysis": null,  // AI 공급자를 쓰면 { "provider", "enrichedChunks", "imagesAnalyzed" }
  "processedAt": "2026-10-09T15:55:03.5762534Z"
}
```

## 지원 형식

CLI 는 FileFlux 라이브러리가 읽는 형식을 그대로 읽는다 — 목록은 [FileFlux README 의 지원 형식 표](../../README.md)를 본다.
`fileflux info <file>` 의 «Supported format» 도 같은 리더 목록에서 답한다.

## 종료 코드

| 코드 | 뜻 |
|---|---|
| `0` | 명령이 끝까지 실행됐다 (입력에 청크·QA 쌍이 없다는 경고만 낸 경우 포함) |
| `1` | 명령이 실패했다 — 잘못된 인자(그 명령이 쓰지 않는 `-f` 형식 포함), 입력 파일 없음, AI 공급자 미설정·초기화 실패, 문서를 읽거나 처리하다 난 오류, 알 수 없는 설정 키 |

스크립트·CI 에서 실패를 종료 코드로 판정할 수 있다:

```bash
fileflux extract report.pdf -o out -q || echo "extract failed"
```

## 사용 예제

### 기본 문서 처리

```bash
# 문서 정보 확인
fileflux info report.pdf

# 빠른 텍스트 추출
fileflux extract report.pdf

# 기본 청킹 (AI 없음)
fileflux chunk report.pdf -s Paragraph -m 512

# AI 강화와 함께 완전한 처리
fileflux process report.pdf -s Auto -m 512
```

### 고급 워크플로우

```bash
# 대용량 문서 (큰 청크)
fileflux process large-doc.pdf -m 2048 -l 256

# 정밀 청킹 (작은 청크, 큰 중복)
fileflux chunk document.pdf -m 256 -l 128

# AI 강화 없는 배치 처리
for file in *.pdf; do
  fileflux process "$file" --no-enrich -q
done

# JSONL 출력 (스트리밍에 적합)
fileflux process document.pdf -f jsonl -o out
```

### RAG 시스템 통합

```bash
# 1단계: 문서 처리 및 청킹
fileflux process knowledge-base.pdf -s Hierarchical -m 512 -f json -o out

# 2단계: 청크를 벡터 데이터베이스에 로드
# (별도 도구 사용: Pinecone, Weaviate, etc.)

# 3단계: AI 강화로 메타데이터 품질 향상
fileflux process knowledge-base.pdf -s Semantic -f json -o out
```

## 성능 팁

1. **대용량 문서**: 더 큰 청크 크기 사용 (`-m 2048`)
2. **배치 처리**: `--quiet` 플래그로 출력 감소
3. **AI 비용**: `chunk` 는 필요할 때만 `--ai --enrich`, `process` 는 필요 없으면 `--no-ai`
4. **스트리밍**: 대용량 출력에 JSONL 형식 사용 (`-f jsonl`)
5. **전략 선택**:
   - 빠른 처리: `Token` 또는 `Paragraph`
   - 제목 구조가 있는 문서: `Hierarchical`
   - 의미 경계: `Semantic` (임베더 필요)

## 문제 해결

### "No AI provider configured"

AI 강화를 사용하려면 환경 변수를 설정하세요:

```bash
# OpenAI
export OPENAI_API_KEY="sk-..."

# 또는 Anthropic · Google (위 «AI 공급자 설정» 참조)
export ANTHROPIC_API_KEY="sk-ant-..."
```

### "File not found"

절대 경로를 사용하거나 파일이 존재하는지 확인하세요:

```bash
fileflux info "C:\Documents\report.pdf"
fileflux info "./documents/report.pdf"
```

### "Unknown chunking strategy 'X'"

유효한 전략 사용: Auto, Sentence, Paragraph, Token, Semantic, Hierarchical

```bash
fileflux chunk document.pdf -s Auto
```

## 개발자 정보

### 프로젝트 구조

```
FileFlux.CLI/
├── Commands/           # CLI 명령 구현
│   ├── ExtractCommand.cs
│   ├── ChunkCommand.cs
│   ├── ProcessCommand.cs
│   └── InfoCommand.cs
├── Services/           # AI 공급자 및 설정
│   ├── CliEnvironmentConfig.cs
│   ├── AIProviderFactory.cs
│   └── Providers/
│       └── OpenAIDocumentAnalysisService.cs
└── Program.cs          # 진입점

출력 파일은 라이브러리의 `FileSystemOutputWriter` 가 쓴다.
```

### 아키텍처 원칙

FileFlux CLI는 **FileFlux SDK의 소비 앱**입니다:

- FileFlux는 인터페이스만 정의 (`IDocumentAnalysisService`, `IImageToTextService`)
- CLI가 구현체 제공 (OpenAI, Anthropic)
- 공식 SDK 사용 (OpenAI SDK, Anthropic SDK)
- DI를 통한 느슨한 결합

### 빌드 및 패키징

```bash
# 빌드
dotnet build

# 로컬 도구로 설치
dotnet pack
dotnet tool install --global --add-source ./nupkg FileFlux.CLI

# NuGet에 게시
dotnet nuget push FileFlux.CLI.*.nupkg --api-key <key> --source https://api.nuget.org/v3/index.json
```

## 라이선스

MIT License - 자세한 내용은 [LICENSE](../../LICENSE) 참조

## 링크

- [FileFlux SDK 문서](../../README.md)
- [아키텍처 가이드](../../docs/ARCHITECTURE.md)
- [튜토리얼](../../docs/TUTORIAL.md)
