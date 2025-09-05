# 문서 구조화 명세서

## 개요

FileFlux는 다양한 문서 형식을 일관된 구조로 변환하여 RAG 시스템에 최적화된 텍스트 청크를 생성합니다. 각 문서 유형별로 특화된 구조화 전략을 적용하되, 최종 출력은 표준화된 형식을 따릅니다.

## 표준 청크 구조

모든 문서 형식은 다음과 같은 일관된 구조로 변환됩니다:

```
DOCUMENT_TYPE: [문서 유형]
SECTION_TYPE: [섹션 유형]
TITLE: [제목 또는 섹션명]
DESCRIPTION: [내용 설명 또는 요약]
SOURCE: [원본 위치 정보]
CONTEXT: [문맥 정보]
CONTENT: [실제 내용]
METADATA: [추가 메타데이터]
```

## 문서 유형별 구조화 전략

### 1. 기술 문서 (Markdown, TXT)

**특징**: 구조화된 텍스트, 제목 계층, 코드 블록

**구조화 전략**:
```
DOCUMENT_TYPE: TECHNICAL_DOCUMENT
SECTION_TYPE: HEADING_L[1-6] | CODE_BLOCK | PARAGRAPH | LIST
TITLE: [H1-H6 제목 또는 섹션 이름]
DESCRIPTION: [섹션 내용 요약]
SOURCE: [파일명:라인번호]
CONTEXT: [상위 섹션 경로]
CONTENT: [실제 텍스트 내용]
METADATA: {
  "heading_level": 1-6,
  "has_code": true/false,
  "list_type": "ordered|unordered|none",
  "estimated_tokens": 150
}
```

**예시**:
```
DOCUMENT_TYPE: TECHNICAL_DOCUMENT
SECTION_TYPE: HEADING_L2
TITLE: Installation Guide
DESCRIPTION: Step-by-step installation instructions for FileFlux SDK
SOURCE: README.md:25-45
CONTEXT: Getting Started > Installation Guide
CONTENT: To install FileFlux, use the following NuGet command...
METADATA: {"heading_level": 2, "has_code": true, "estimated_tokens": 120}
```

### 2. 오피스 문서 (DOCX, PDF)

**특징**: 서식 정보, 표, 이미지, 복잡한 레이아웃

**구조화 전략**:
```
DOCUMENT_TYPE: OFFICE_DOCUMENT
SECTION_TYPE: TITLE | SUBTITLE | PARAGRAPH | TABLE | IMAGE | LIST
TITLE: [추출된 제목 또는 자동 생성]
DESCRIPTION: [LLM 기반 내용 요약]
SOURCE: [페이지번호:섹션위치]
CONTEXT: [문서 구조 내 위치]
CONTENT: [텍스트 내용 + 표 데이터]
METADATA: {
  "page_number": 3,
  "has_table": true,
  "has_image": false,
  "formatting": ["bold", "italic"],
  "estimated_tokens": 200
}
```

**예시**:
```
DOCUMENT_TYPE: OFFICE_DOCUMENT
SECTION_TYPE: TABLE
TITLE: Financial Summary Q3 2024
DESCRIPTION: Quarterly financial performance metrics and comparison
SOURCE: report.docx:page3:table1
CONTEXT: Financial Analysis > Quarterly Results > Q3 Summary
CONTENT: | Metric | Q2 2024 | Q3 2024 | Change |
         | Revenue | $2.1M | $2.8M | +33% |
         | Profit | $420K | $650K | +55% |
METADATA: {"page_number": 3, "has_table": true, "columns": 4, "rows": 3}
```

### 3. 구조화 데이터 (JSON, CSV, XML)

**특징**: 스키마 기반 데이터, 키-값 구조, 반복 패턴

**구조화 전략**:
```
DOCUMENT_TYPE: STRUCTURED_DATA
SECTION_TYPE: SCHEMA | RECORD | FIELD_GROUP | METADATA_SECTION
TITLE: [스키마명 또는 레코드 식별자]
DESCRIPTION: [데이터 구조 설명]
SOURCE: [파일명:경로]
CONTEXT: [데이터 계층 구조]
CONTENT: [구조화된 텍스트 표현]
METADATA: {
  "schema_type": "json|csv|xml",
  "field_count": 5,
  "record_count": 100,
  "data_types": ["string", "number", "date"]
}
```

**예시**:
```
DOCUMENT_TYPE: STRUCTURED_DATA
SECTION_TYPE: RECORD
TITLE: User Profile Record - ID: 12345
DESCRIPTION: Complete user profile with preferences and activity data
SOURCE: users.json:users[0]
CONTEXT: User Data > Profile Records > Active Users
CONTENT: User John Doe (john@example.com) registered on 2024-01-15. 
         Preferences: notifications enabled, theme dark, language English.
         Last activity: 2024-09-02, total sessions: 47
METADATA: {"schema_type": "json", "field_count": 8, "user_id": 12345}
```

### 4. 코드 문서 (Source Code, API Docs)

**특징**: 함수, 클래스, API 엔드포인트, 예제 코드

**구조화 전략**:
```
DOCUMENT_TYPE: CODE_DOCUMENTATION
SECTION_TYPE: API_ENDPOINT | CLASS | METHOD | EXAMPLE | COMMENT
TITLE: [함수명, 클래스명, 엔드포인트명]
DESCRIPTION: [기능 설명 및 사용법]
SOURCE: [파일명:라인범위]
CONTEXT: [네임스페이스 또는 모듈 경로]
CONTENT: [코드 + 설명]
METADATA: {
  "language": "csharp",
  "type": "method|class|interface",
  "parameters": ["param1", "param2"],
  "return_type": "string"
}
```

**예시**:
```
DOCUMENT_TYPE: CODE_DOCUMENTATION  
SECTION_TYPE: METHOD
TITLE: ProcessAsync Method
DESCRIPTION: Asynchronously processes documents and returns chunked results
SOURCE: DocumentProcessor.cs:45-60
CONTEXT: FileFlux > IDocumentProcessor > ProcessAsync
CONTENT: public async Task<IEnumerable<DocumentChunk>> ProcessAsync(
           string filePath, ChunkingOptions options = null) 
         Processes the specified file and returns document chunks optimized for RAG
METADATA: {"language": "csharp", "type": "method", "return_type": "Task<IEnumerable<DocumentChunk>>"}
```

### 5. 이미지 문서 (OCR 기반)

**특징**: 텍스트 추출, 레이아웃 인식, 품질 가변성

**구조화 전략**:
```
DOCUMENT_TYPE: IMAGE_DOCUMENT
SECTION_TYPE: OCR_TEXT | LAYOUT_SECTION | VISUAL_ELEMENT
TITLE: [이미지 파일명 또는 인식된 제목]
DESCRIPTION: [LLM 기반 이미지 내용 설명]
SOURCE: [파일명:영역좌표]
CONTEXT: [이미지 내 위치 정보]
CONTENT: [OCR 추출 텍스트]
METADATA: {
  "ocr_confidence": 0.95,
  "image_type": "document|diagram|screenshot",
  "resolution": "1920x1080",
  "text_regions": 5
}
```

## 구조화 품질 지표

각 청크는 다음 품질 지표를 포함합니다:

### 1. 신뢰성 점수 (Confidence Score)
- **범위**: 0.0 - 1.0
- **계산 요소**: OCR 정확도, LLM 확신도, 구조 인식 정확도

### 2. 완성도 지표 (Completeness Score)
- **범위**: 0.0 - 1.0  
- **계산 요소**: 메타데이터 완성도, 컨텍스트 정보 완성도

### 3. 일관성 점수 (Consistency Score)
- **범위**: 0.0 - 1.0
- **계산 요소**: 표준 구조 준수도, 메타데이터 일관성

## LLM 통합 전략

### 1. 구조 분석
- **목적**: 문서의 논리적 구조 파악
- **프롬프트**: "이 문서의 섹션 구조를 분석하고 각 부분의 역할을 설명하세요"

### 2. 내용 요약
- **목적**: 각 청크의 DESCRIPTION 생성  
- **프롬프트**: "다음 내용을 RAG 시스템에서 검색하기 좋도록 1-2문장으로 요약하세요"

### 3. 메타데이터 추출
- **목적**: 구조화된 메타데이터 생성
- **프롬프트**: "이 텍스트의 문서 유형, 주요 키워드, 중요도를 JSON 형태로 추출하세요"