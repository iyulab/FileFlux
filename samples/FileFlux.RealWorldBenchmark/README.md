# FileFlux Real-World Benchmark

This benchmark tests FileFlux's document processing capabilities with real files and optional LLM integration.

## Setup

### 1. Configure API Keys (Optional)

To use real LLM services for enhanced document analysis:

1. Copy the `.env.local.example` to `.env.local`:
```bash
cp .env.local.example .env.local
```

2. Edit `.env.local` and add your API keys:
```env
OPENAI_API_KEY=sk-your-actual-api-key-here
OPENAI_MODEL=gpt-3.5-turbo
```

### 2. Add Test Files

Place test documents in `D:\data\FileFlux\test\`:
- PDF files (`.pdf`)
- Word documents (`.docx`)
- Markdown files (`.md`)
- Excel files (`.xlsx`)
- PowerPoint files (`.pptx`)

## Running the Benchmark

### Basic Run (Mock Services)
```bash
dotnet run --project samples/FileFlux.RealWorldBenchmark -c Release
```

### With OpenAI Integration
1. Set up your `.env.local` file with valid API key
2. Run the benchmark:
```bash
dotnet run --project samples/FileFlux.RealWorldBenchmark -c Release
```

## Benchmark Metrics

The benchmark measures:

### Performance Metrics
- **Processing Time**: Time to process each file
- **Throughput**: MB/s processing speed
- **Memory Usage**: Peak memory consumption
- **Cache Effectiveness**: Speed improvement from caching

### RAG Quality Metrics
- **Semantic Completeness**: How well chunks preserve complete thoughts (0-100%)
- **Context Preservation**: Overlap quality between chunks (0-100%)
- **Information Density**: Ratio of meaningful content (0-100%)
- **Overall Score**: Weighted average of quality metrics

### Target Goals
- **Retrieval Recall**: ≥85%
- **Chunk Completeness**: ≥90%
- **Memory Efficiency**: ≤50% of file size
- **Processing Speed**: ≥10 MB/s

## Understanding Results

### Quality Scores
- **🟢 Green (80-100%)**: Excellent quality, ready for production
- **🟡 Yellow (60-79%)**: Good quality, may need minor adjustments
- **🔴 Red (<60%)**: Poor quality, needs optimization

### Strategy Comparison
- **Intelligent**: Best for RAG systems, uses LLM for boundary detection
- **Semantic**: Preserves sentence boundaries, good for natural text
- **FixedSize**: Consistent chunk sizes, predictable memory usage
- **Paragraph**: Maintains document structure, good for formatted text

## Optimization Tips

1. **Low Completeness Score**: 
   - Increase chunk size
   - Use Intelligent or Semantic strategy
   - Enable LLM-based parsing

2. **Poor Overlap Quality**:
   - Increase overlap size (128-256 tokens recommended)
   - Use Intelligent strategy with adaptive overlap

3. **High Memory Usage**:
   - Enable streaming mode
   - Reduce chunk size
   - Use MemoryEfficientProcessor

4. **Slow Processing**:
   - Enable parallel processing
   - Use cache for repeated processing
   - Consider FixedSize strategy for speed

## API Cost Estimation

When using OpenAI API:
- **GPT-3.5-turbo**: ~$0.001 per 1000 tokens
- **Average document**: 10-50 API calls
- **Estimated cost**: $0.01-0.05 per document

## Troubleshooting

### "API key not found" warning
- Check `.env.local` file exists
- Verify API key is correct
- Ensure no spaces or quotes around the key

### Poor quality scores with real LLM
- Check API response times (may affect chunking)
- Verify model selection (GPT-4 generally better than GPT-3.5)
- Consider adjusting temperature settings

### Out of memory errors
- Use streaming mode for large files
- Reduce concurrent processing
- Increase system memory allocation