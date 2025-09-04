namespace FileFlux.SampleApp;

/// <summary>
/// 콘솔에서 진행률 바를 표시하는 유틸리티 클래스
/// </summary>
public class ConsoleProgressBar
{
    private readonly int _width;
    private readonly char _completeChar;
    private readonly char _incompleteChar;
    private int _lastLineLength = 0;

    /// <summary>
    /// ConsoleProgressBar 인스턴스를 초기화합니다
    /// </summary>
    /// <param name="width">진행률 바의 너비 (기본값: 50)</param>
    /// <param name="completeChar">완료된 부분을 나타내는 문자 (기본값: '█')</param>
    /// <param name="incompleteChar">미완료 부분을 나타내는 문자 (기본값: '░')</param>
    public ConsoleProgressBar(int width = 50, char completeChar = '█', char incompleteChar = '░')
    {
        _width = width;
        _completeChar = completeChar;
        _incompleteChar = incompleteChar;
    }

    /// <summary>
    /// 진행률을 업데이트합니다
    /// </summary>
    /// <param name="progress">진행률 (0.0 - 1.0)</param>
    /// <param name="stage">현재 단계</param>
    /// <param name="message">진행률 메시지</param>
    public void UpdateProgress(double progress, string stage, string message)
    {
        // 진행률 범위 제한
        progress = Math.Max(0.0, Math.Min(1.0, progress));

        // 완료된 문자 수 계산
        var completeChars = (int)Math.Round(progress * _width);
        var incompleteChars = _width - completeChars;

        // 진행률 바 구성
        var progressBar = new string(_completeChar, completeChars) +
                         new string(_incompleteChar, incompleteChars);

        // 백분율 계산
        var percentage = Math.Round(progress * 100, 1);

        // 메시지 길이 제한 (콘솔 너비 고려)
        var maxMessageLength = 50; // 기본값으로 고정
        try
        {
            maxMessageLength = Math.Max(30, Console.WindowWidth - _width - 20);
        }
        catch
        {
            // 콘솔 윈도우 정보를 가져올 수 없는 경우 기본값 사용
        }

        if (message.Length > maxMessageLength)
        {
            message = message.Substring(0, maxMessageLength - 3) + "...";
        }

        // 진행률 바 라인 구성
        var progressLine = $"\r[{progressBar}] {percentage,5:F1}% {stage,-12} {message}";

        // 이전 라인보다 짧으면 공백으로 채우기
        if (progressLine.Length < _lastLineLength)
        {
            progressLine = progressLine.PadRight(_lastLineLength);
        }

        _lastLineLength = progressLine.Length;

        // 콘솔에 출력 (커서를 줄 시작으로 이동)
        Console.Write(progressLine);
    }

    /// <summary>
    /// 진행률 바를 완료 상태로 표시합니다
    /// </summary>
    public void Complete()
    {
        UpdateProgress(1.0, "완료", "처리가 완료되었습니다.");
        Console.WriteLine(); // 새 줄로 이동
    }

    /// <summary>
    /// 진행률 바를 오류 상태로 표시합니다
    /// </summary>
    /// <param name="errorMessage">오류 메시지</param>
    public void ShowError(string errorMessage)
    {
        // 빨간색으로 오류 표시 (지원되는 경우)
        try
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n❌ 오류: {errorMessage}");
        }
        finally
        {
            Console.ResetColor();
        }
    }

    /// <summary>
    /// 진행률 바를 지웁니다
    /// </summary>
    public void Clear()
    {
        if (_lastLineLength > 0)
        {
            Console.Write("\r" + new string(' ', _lastLineLength) + "\r");
            _lastLineLength = 0;
        }
    }

    /// <summary>
    /// 여러 단계의 진행률을 표시하는 고급 진행률 바
    /// </summary>
    public class MultiStageProgressBar
    {
        private readonly ConsoleProgressBar _progressBar;
        private readonly List<StageInfo> _stages;
        private int _currentStageIndex = 0;

        public MultiStageProgressBar(params string[] stageNames)
        {
            _progressBar = new ConsoleProgressBar();
            _stages = stageNames.Select((name, index) => new StageInfo
            {
                Name = name,
                Index = index,
                Progress = 0.0
            }).ToList();
        }

        /// <summary>
        /// 현재 단계의 진행률을 업데이트합니다
        /// </summary>
        /// <param name="stageProgress">현재 단계의 진행률 (0.0 - 1.0)</param>
        /// <param name="message">진행률 메시지</param>
        public void UpdateCurrentStage(double stageProgress, string message = "")
        {
            if (_currentStageIndex < _stages.Count)
            {
                _stages[_currentStageIndex].Progress = Math.Max(0.0, Math.Min(1.0, stageProgress));

                // 전체 진행률 계산
                var completedStagesProgress = _currentStageIndex / (double)_stages.Count;
                var currentStageProgress = stageProgress / _stages.Count;
                var overallProgress = completedStagesProgress + currentStageProgress;

                var stageName = _stages[_currentStageIndex].Name;
                _progressBar.UpdateProgress(overallProgress, stageName, message);
            }
        }

        /// <summary>
        /// 다음 단계로 이동합니다
        /// </summary>
        /// <param name="message">단계 이동 메시지</param>
        public void NextStage(string message = "")
        {
            if (_currentStageIndex < _stages.Count - 1)
            {
                _stages[_currentStageIndex].Progress = 1.0; // 현재 단계 완료
                _currentStageIndex++;

                UpdateCurrentStage(0.0, message);
            }
        }

        /// <summary>
        /// 모든 단계를 완료합니다
        /// </summary>
        public void Complete()
        {
            _currentStageIndex = _stages.Count - 1;
            UpdateCurrentStage(1.0, "완료");
            _progressBar.Complete();
        }

        /// <summary>
        /// 현재 단계 이름을 반환합니다
        /// </summary>
        public string CurrentStageName =>
            _currentStageIndex < _stages.Count ? _stages[_currentStageIndex].Name : "완료";

        private class StageInfo
        {
            public string Name { get; set; } = string.Empty;
            public int Index { get; set; }
            public double Progress { get; set; }
        }
    }
}