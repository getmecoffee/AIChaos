using AIChaos.Brain.Models;

namespace AIChaos.Brain.Services;

/// <summary>
/// Service for managing test client connections and command routing.
/// </summary>
public class TestClientService
{
    private readonly SettingsService _settingsService;
    private readonly ILogger<TestClientService> _logger;
    
    // Queue for commands waiting to be tested
    private readonly List<(int CommandId, string Code, bool CleanupAfterTest)> _testQueue = new();
    
    // Commands that passed testing and are ready for main client
    private readonly List<(int CommandId, string Code)> _approvedQueue = new();
    
    // Track which commands are currently being tested
    private readonly Dictionary<int, PendingTest> _pendingTests = new();
    
    private readonly object _lock = new();
    
    public TestClientService(SettingsService settingsService, ILogger<TestClientService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }
    
    /// <summary>
    /// Checks if test client mode is enabled.
    /// </summary>
    public bool IsEnabled => _settingsService.Settings.TestClient.Enabled;
    
    /// <summary>
    /// Checks if test client is currently connected (polled recently).
    /// </summary>
    public bool IsConnected
    {
        get
        {
            var lastPoll = _settingsService.Settings.TestClient.LastPollTime;
            if (!lastPoll.HasValue) return false;
            // Consider connected if polled in the last 30 seconds
            return (DateTime.UtcNow - lastPoll.Value).TotalSeconds < 30;
        }
    }
    
    /// <summary>
    /// Adds a command to the test queue. If test client mode is disabled, adds directly to approved queue.
    /// </summary>
    public void QueueForTesting(int commandId, string code)
    {
        lock (_lock)
        {
            if (!IsEnabled)
            {
                // If test client mode is disabled, skip testing
                _approvedQueue.Add((commandId, code));
                return;
            }
            
            var settings = _settingsService.Settings.TestClient;
            _testQueue.Add((commandId, code, settings.CleanupAfterTest));
            _logger.LogInformation("[TEST CLIENT] Command #{CommandId} queued for testing", commandId);
        }
    }
    
    /// <summary>
    /// Polls for the next command to test (called by test client).
    /// </summary>
    public TestPollResponse? PollTestCommand()
    {
        lock (_lock)
        {
            // Update last poll time
            _settingsService.UpdateTestClientConnection(true);
            
            if (_testQueue.Count == 0)
            {
                return null;
            }
            
            var (commandId, code, cleanupAfterTest) = _testQueue[0];
            _testQueue.RemoveAt(0);
            
            // Track pending test
            _pendingTests[commandId] = new PendingTest
            {
                CommandId = commandId,
                Code = code,
                StartedAt = DateTime.UtcNow,
                TimeoutSeconds = _settingsService.Settings.TestClient.TimeoutSeconds
            };
            
            _logger.LogInformation("[TEST CLIENT] Sending command #{CommandId} to test client", commandId);
            
            return new TestPollResponse
            {
                HasCode = true,
                Code = code,
                CommandId = commandId,
                CleanupAfterTest = cleanupAfterTest
            };
        }
    }
    
    /// <summary>
    /// Reports test result from test client.
    /// </summary>
    public TestResultAction ReportTestResult(int commandId, bool success, string? error)
    {
        lock (_lock)
        {
            if (!_pendingTests.TryGetValue(commandId, out var pending))
            {
                _logger.LogWarning("[TEST CLIENT] Received result for unknown command #{CommandId}", commandId);
                return TestResultAction.Unknown;
            }
            
            _pendingTests.Remove(commandId);
            
            if (success)
            {
                // Test passed! Queue for main client
                _approvedQueue.Add((commandId, pending.Code));
                _logger.LogInformation("[TEST CLIENT] Command #{CommandId} PASSED testing - queued for main client", commandId);
                return TestResultAction.Approved;
            }
            else
            {
                // Test failed - don't send to main client
                _logger.LogWarning("[TEST CLIENT] Command #{CommandId} FAILED testing: {Error}", commandId, error);
                return TestResultAction.Rejected;
            }
        }
    }
    
    /// <summary>
    /// Polls for the next approved command (called by main client when test mode is enabled).
    /// </summary>
    public (int CommandId, string Code)? PollApprovedCommand()
    {
        lock (_lock)
        {
            if (_approvedQueue.Count == 0)
            {
                return null;
            }
            
            var result = _approvedQueue[0];
            _approvedQueue.RemoveAt(0);
            return result;
        }
    }
    
    /// <summary>
    /// Checks for timed out tests and marks them as failed.
    /// </summary>
    public void CheckTimeouts()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var timedOut = _pendingTests
                .Where(kvp => (now - kvp.Value.StartedAt).TotalSeconds > kvp.Value.TimeoutSeconds)
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var commandId in timedOut)
            {
                _pendingTests.Remove(commandId);
                _logger.LogWarning("[TEST CLIENT] Command #{CommandId} timed out - not sending to main client", commandId);
            }
        }
    }
    
    private class PendingTest
    {
        public int CommandId { get; set; }
        public string Code { get; set; } = "";
        public DateTime StartedAt { get; set; }
        public int TimeoutSeconds { get; set; }
    }
}

/// <summary>
/// Response for test client poll.
/// </summary>
public class TestPollResponse
{
    public bool HasCode { get; set; }
    public string? Code { get; set; }
    public int? CommandId { get; set; }
    public bool CleanupAfterTest { get; set; }
}

/// <summary>
/// What action was taken after a test result.
/// </summary>
public enum TestResultAction
{
    Unknown,
    Approved,
    Rejected
}
