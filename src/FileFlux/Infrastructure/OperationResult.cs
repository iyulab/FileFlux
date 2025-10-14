namespace FileFlux.Infrastructure;

/// <summary>
/// 작업 결과를 나타내는 내부 헬퍼 클래스
/// AsyncEnumerable에서 예외 처리를 위해 사용
/// </summary>
/// <typeparam name="T">결과 타입</typeparam>
internal class OperationResult<T>
{
    public bool IsSuccess { get; private set; }
    public T? Content { get; private set; }
    public string? ErrorMessage { get; private set; }

    private OperationResult(bool isSuccess, T? content, string? errorMessage)
    {
        IsSuccess = isSuccess;
        Content = content;
        ErrorMessage = errorMessage;
    }

    public static OperationResult<T> Success(T content)
    {
        return new OperationResult<T>(true, content, null);
    }

    public static OperationResult<T> Failure(string errorMessage)
    {
        return new OperationResult<T>(false, default, errorMessage);
    }
}