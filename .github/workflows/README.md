# GitHub Actions Workflows

## CI/CD 최적화 전략

### 🚀 테스트 단계 제외

현재 CI/CD 파이프라인은 **비용 및 시간 절감**을 위해 테스트 단계를 의도적으로 제외하고 있습니다.

#### 이유:
- **시간 절약**: 248개의 테스트 실행은 상당한 시간이 소요됨
- **비용 절감**: GitHub Actions 무료 사용량 절약
- **API 비용**: OpenAI API를 사용하는 RAG 품질 테스트는 추가 비용 발생
- **로컬 검증**: 개발자가 로컬에서 충분히 테스트 후 푸시

#### 현재 파이프라인 구조:
```yaml
1. 🔍 Detect Version Changes - 버전 감지
2. 🔨 Build Solution - 빌드만 수행 (테스트 제외)
3. 📦 Build NuGet Package - 패키지 생성
4. 🚀 Publish to NuGet.org - NuGet 게시
5. 📝 Create GitHub Release - 릴리즈 생성
```

### 로컬 테스트 실행 방법

배포 전 로컬에서 테스트를 실행하세요:

```bash
# 모든 테스트 실행
dotnet test src/FileFlux.sln

# 빠른 테스트만 실행 (Performance 제외)
dotnet test src/FileFlux.sln --filter "Category!=Performance"

# 특정 카테고리만 실행
dotnet test src/FileFlux.sln --filter "Category=Unit"
```

### 테스트 카테고리

- **Unit**: 단위 테스트 (빠름)
- **Integration**: 통합 테스트 (중간)
- **Performance**: 성능 테스트 (느림, 제외 권장)
- **RAGQuality**: RAG 품질 테스트 (API 필요)
- **RAGQualityAdvanced**: 고급 RAG 테스트 (실제 API 필요)

### 테스트를 CI/CD에 추가하려면

만약 나중에 테스트를 추가하고 싶다면, `nuget-publish.yml`의 build job에 다음 단계를 추가하세요:

```yaml
# 테스트 실행 (현재는 비활성화)
- name: 🧪 Run Tests
  run: dotnet test src/ --configuration ${{ env.BUILD_CONFIGURATION }} --no-build --filter "Category!=Performance&Category!=RAGQualityAdvanced"
  continue-on-error: true  # 테스트 실패해도 계속 진행
```

### 권장 사항

1. **로컬 테스트 필수**: PR 생성 전 로컬에서 테스트 통과 확인
2. **선택적 테스트**: 중요한 릴리즈 전에만 전체 테스트 실행
3. **API 테스트 제외**: RAG 품질 테스트는 로컬에서만 실행
4. **성능 테스트 제외**: Performance 카테고리는 항상 제외

### 현재 상태

✅ **최적화 완료**: 테스트 단계가 제외되어 빠르고 효율적인 배포 가능