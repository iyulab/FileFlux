# FileFlux CLI

Command-line interface for FileFlux document processing SDK. Extract, chunk, and process documents for RAG systems with AI-powered metadata enrichment.

## 설치

### NuGet 패키지로 설치 (권장)

```bash
dotnet tool install --global FileFlux.CLI
```

### 소스에서 빌드

```bash
cd src/FileFlux.CLI
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
fileflux extract document.pdf -o output.json
fileflux extract document.docx -f markdown
fileflux extract document.pdf -f jsonl -q
```

**옵션**:
- `-o, --output <path>` - 출력 파일 경로 (기본값: input.extracted.json)
- `-f, --format <format>` - 출력 형식: json, jsonl, markdown (기본값: json)
- `-q, --quiet` - 최소 출력

**특징**:
- AI 불필요 (기본 처리만)
- 대용량 청크 (100,000자)로 추출
- 메타데이터 포함

### `chunk` - 지능형 청킹

문서를 지능적으로 청크로 분할합니다.

```bash
fileflux chunk document.pdf -s Smart -m 512
fileflux chunk document.pdf --enrich --strategy Auto
fileflux chunk document.docx -m 1024 --overlap 128
```

**옵션**:
- `-o, --output <path>` - 출력 파일 경로 (기본값: input.chunks.json)
- `-f, --format <format>` - 출력 형식: json, jsonl, markdown (기본값: json)
- `-s, --strategy <strategy>` - 청킹 전략: Auto, Smart, Intelligent, Semantic, Paragraph, FixedSize (기본값: Auto)
- `-m, --max-size <size>` - 최대 청크 크기 (토큰 단위, 기본값: 512)
- `--overlap <size>` - 청크 간 중복 크기 (기본값: 64)
- `--enrich` - AI 메타데이터 강화 활성화 (AI 공급자 필요)
- `-q, --quiet` - 최소 출력

**청킹 전략**:
- `Auto` - 문서 유형에 따라 자동 선택
- `Smart` - 구조 인식 청킹
- `Intelligent` - 의미 기반 청킹 (AI 권장)
- `Semantic` - 완전 의미론적 청킹 (AI 필요)
- `Paragraph` - 단락 단위 청킹
- `FixedSize` - 고정 크기 청킹

### `process` - 완전한 파이프라인

추출, 청킹, 강화를 포함한 완전한 처리 파이프라인입니다.

```bash
fileflux process document.pdf -o output.json
fileflux process document.pdf --no-enrich
fileflux process document.docx -s Auto -m 512 --overlap 64
```

**옵션**:
- `-o, --output <path>` - 출력 파일 경로 (기본값: input.processed.json)
- `-f, --format <format>` - 출력 형식: json, jsonl, markdown (기본값: json)
- `-s, --strategy <strategy>` - 청킹 전략 (기본값: Auto)
- `-m, --max-size <size>` - 최대 청크 크기 (기본값: 512)
- `--overlap <size>` - 중복 크기 (기본값: 64)
- `--no-enrich` - AI 강화 비활성화
- `-q, --quiet` - 최소 출력

**기본 동작**:
- AI 공급자가 설정된 경우 메타데이터 강화 활성화
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

### Anthropic 설정 (향후 지원 예정)

```bash
# Windows (PowerShell)
$env:ANTHROPIC_API_KEY = "sk-ant-..."
$env:ANTHROPIC_MODEL = "claude-3-haiku-20240307"  # 선택사항

# Linux/Mac
export ANTHROPIC_API_KEY="sk-ant-..."
export ANTHROPIC_MODEL="claude-3-haiku-20240307"  # 선택사항
```

### 환경 변수 우선순위

CLI는 다음 순서로 환경 변수를 확인합니다:

1. `FILEFLUX_OPENAI_API_KEY` → `OPENAI_API_KEY` → `API_KEY`
2. `FILEFLUX_PROVIDER` (명시적 공급자 지정)

## 출력 형식

### JSON (기본값)

```json
[
  {
    "id": "chunk-1",
    "content": "Document content...",
    "metadata": {
      "chunkIndex": 0,
      "totalChunks": 10,
      "startPosition": 0,
      "endPosition": 512,
      "customProperties": {
        "enriched_topics": ["AI", "machine learning"],
        "quality_score": 0.95
      }
    }
  }
]
```

### JSONL (JSON Lines)

각 청크가 한 줄에 하나씩, 스트리밍 처리에 적합:

```jsonl
{"id":"chunk-1","content":"...","metadata":{...}}
{"id":"chunk-2","content":"...","metadata":{...}}
```

### Markdown

사람이 읽기 쉬운 형식:

```markdown
# Document: document.pdf

## Chunk 1/10

**Content**:
Document content...

**Metadata**:
- Chunk Index: 0
- Position: 0-512
- Topics: AI, machine learning
- Quality Score: 0.95
```

## 지원 형식

- **PDF** (.pdf) - 텍스트 및 이미지 추출
- **Word** (.docx) - 전체 서식 및 메타데이터
- **Excel** (.xlsx) - 시트, 셀, 수식
- **PowerPoint** (.pptx) - 슬라이드 및 노트
- **Markdown** (.md) - 구조 보존
- **Text** (.txt) - 일반 텍스트
- **JSON** (.json) - 구조화된 데이터
- **CSV** (.csv) - 표 형식 데이터
- **HTML** (.html, .htm) - 웹 콘텐츠
- **ZIP** (.zip) - 압축 파일 내 문서 (재귀 처리)

## 사용 예제

### 기본 문서 처리

```bash
# 문서 정보 확인
fileflux info report.pdf

# 빠른 텍스트 추출
fileflux extract report.pdf -f markdown

# 기본 청킹 (AI 없음)
fileflux chunk report.pdf -s Smart -m 512

# AI 강화와 함께 완전한 처리
fileflux process report.pdf -s Auto -m 512
```

### 고급 워크플로우

```bash
# 대용량 문서 (큰 청크)
fileflux process large-doc.pdf -m 2048 --overlap 256

# 정밀 청킹 (작은 청크, 큰 중복)
fileflux chunk document.pdf -m 256 --overlap 128

# AI 강화 없는 배치 처리
for file in *.pdf; do
  fileflux process "$file" --no-enrich -q
done

# JSONL 출력 (스트리밍에 적합)
fileflux process document.pdf -f jsonl -o output.jsonl
```

### RAG 시스템 통합

```bash
# 1단계: 문서 처리 및 청킹
fileflux process knowledge-base.pdf -s Intelligent -m 512 -o chunks.json

# 2단계: 청크를 벡터 데이터베이스에 로드
# (별도 도구 사용: Pinecone, Weaviate, etc.)

# 3단계: AI 강화로 메타데이터 품질 향상
fileflux process knowledge-base.pdf --enrich -s Semantic -o enriched.json
```

## 성능 팁

1. **대용량 문서**: 더 큰 청크 크기 사용 (`-m 2048`)
2. **배치 처리**: `--quiet` 플래그로 출력 감소
3. **AI 비용**: 필요시에만 `--enrich` 사용
4. **스트리밍**: 대용량 출력에 JSONL 형식 사용 (`-f jsonl`)
5. **전략 선택**:
   - 빠른 처리: `FixedSize` 또는 `Paragraph`
   - 품질: `Smart` 또는 `Intelligent`
   - 최고 품질: `Semantic` (AI 필요)

## 문제 해결

### "No AI provider configured"

AI 강화를 사용하려면 환경 변수를 설정하세요:

```bash
# OpenAI
export OPENAI_API_KEY="sk-..."

# 또는 Anthropic (향후)
export ANTHROPIC_API_KEY="sk-ant-..."
```

### "File not found"

절대 경로를 사용하거나 파일이 존재하는지 확인하세요:

```bash
fileflux info "C:\Documents\report.pdf"
fileflux info "./documents/report.pdf"
```

### "Strategy 'X' is not supported"

유효한 전략 사용: Auto, Smart, Intelligent, Semantic, Paragraph, FixedSize

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
├── Output/             # 출력 형식 작성기
│   ├── IOutputWriter.cs
│   ├── JsonOutputWriter.cs
│   ├── JsonLinesOutputWriter.cs
│   └── MarkdownOutputWriter.cs
└── Program.cs          # 진입점
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

- [FileFlux SDK 문서](../../docs/README.md)
- [아키텍처 가이드](../../docs/ARCHITECTURE.md)
- [튜토리얼](../../docs/TUTORIAL.md)
- [벤치마크](../../docs/benchmarks/)
