using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIChaos.Brain.Models;

namespace AIChaos.Brain.Services;

/// <summary>
/// Service for managing user accounts, authentication, and YouTube linking.
/// </summary>
public class AccountService
{
    private readonly string _accountsPath;
    private readonly ILogger<AccountService> _logger;
    private readonly ConcurrentDictionary<string, Account> _accounts = new(); // by ID
    private readonly ConcurrentDictionary<string, string> _usernameIndex = new(); // username -> ID
    private readonly ConcurrentDictionary<string, string> _youtubeIndex = new(); // YouTube Channel ID -> Account ID
    private readonly ConcurrentDictionary<string, string> _sessionIndex = new(); // Session Token -> Account ID
    private readonly object _lock = new();

    private const int DEFAULT_RATE_LIMIT_SECONDS = 20;
    private const int VERIFICATION_CODE_EXPIRY_MINUTES = 30;
    private const int SESSION_EXPIRY_DAYS = 30;

    public AccountService(ILogger<AccountService> logger)
    {
        _logger = logger;
        _accountsPath = Path.Combine(AppContext.BaseDirectory, "accounts.json");
        LoadAccounts();
    }

    /// <summary>
    /// Creates a new account.
    /// </summary>
    public (bool Success, string? Error, Account? Account) CreateAccount(string username, string password, string? displayName = null)
    {
        username = username.Trim().ToLowerInvariant();
        
        if (string.IsNullOrEmpty(username) || username.Length < 3)
        {
            return (false, "Username must be at least 3 characters", null);
        }
        
        if (string.IsNullOrEmpty(password) || password.Length < 4)
        {
            return (false, "Password must be at least 4 characters", null);
        }
        
        if (_usernameIndex.ContainsKey(username))
        {
            return (false, "Username already taken", null);
        }
        
        var account = new Account
        {
            Username = username,
            PasswordHash = HashPassword(password),
            DisplayName = displayName ?? username,
            CreatedAt = DateTime.UtcNow
        };
        
        _accounts[account.Id] = account;
        _usernameIndex[username] = account.Id;
        SaveAccounts();
        
        _logger.LogInformation("[ACCOUNT] Created account: {Username} ({Id})", username, account.Id);
        
        return (true, null, account);
    }

    /// <summary>
    /// Authenticates a user and creates a session.
    /// </summary>
    public (bool Success, string? Error, Account? Account, string? SessionToken) Login(string username, string password)
    {
        username = username.Trim().ToLowerInvariant();
        
        if (!_usernameIndex.TryGetValue(username, out var accountId))
        {
            return (false, "Invalid username or password", null, null);
        }
        
        if (!_accounts.TryGetValue(accountId, out var account))
        {
            return (false, "Invalid username or password", null, null);
        }
        
        if (!VerifyPassword(password, account.PasswordHash))
        {
            return (false, "Invalid username or password", null, null);
        }
        
        // Create session token
        var sessionToken = GenerateSessionToken();
        
        lock (account)
        {
            // Remove old session from index
            if (!string.IsNullOrEmpty(account.SessionToken))
            {
                _sessionIndex.TryRemove(account.SessionToken, out _);
            }
            
            account.SessionToken = sessionToken;
            account.SessionExpiresAt = DateTime.UtcNow.AddDays(SESSION_EXPIRY_DAYS);
        }
        
        _sessionIndex[sessionToken] = accountId;
        SaveAccounts();
        
        _logger.LogInformation("[ACCOUNT] Login: {Username}", username);
        
        return (true, null, account, sessionToken);
    }

    /// <summary>
    /// Gets an account by session token.
    /// </summary>
    public Account? GetAccountBySession(string sessionToken)
    {
        if (string.IsNullOrEmpty(sessionToken))
        {
            return null;
        }
        
        if (!_sessionIndex.TryGetValue(sessionToken, out var accountId))
        {
            return null;
        }
        
        if (!_accounts.TryGetValue(accountId, out var account))
        {
            return null;
        }
        
        // Check if session is expired
        if (account.SessionExpiresAt.HasValue && account.SessionExpiresAt.Value < DateTime.UtcNow)
        {
            Logout(sessionToken);
            return null;
        }
        
        return account;
    }

    /// <summary>
    /// Logs out a session.
    /// </summary>
    public void Logout(string sessionToken)
    {
        if (_sessionIndex.TryRemove(sessionToken, out var accountId))
        {
            if (_accounts.TryGetValue(accountId, out var account))
            {
                lock (account)
                {
                    if (account.SessionToken == sessionToken)
                    {
                        account.SessionToken = null;
                        account.SessionExpiresAt = null;
                    }
                }
                SaveAccounts();
            }
        }
    }

    /// <summary>
    /// Generates a verification code for linking a YouTube channel.
    /// </summary>
    public string GenerateYouTubeLinkCode(string accountId)
    {
        if (!_accounts.TryGetValue(accountId, out var account))
        {
            return "";
        }
        
        var code = "LINK-" + GenerateRandomCode(4);
        
        lock (account)
        {
            account.PendingVerificationCode = code;
            account.VerificationCodeExpiresAt = DateTime.UtcNow.AddMinutes(VERIFICATION_CODE_EXPIRY_MINUTES);
        }
        
        SaveAccounts();
        _logger.LogInformation("[ACCOUNT] Generated link code {Code} for {Username}", code, account.Username);
        
        return code;
    }

    /// <summary>
    /// Checks if a Super Chat message contains a verification code and links the channel.
    /// Called by YouTubeService when processing Super Chats.
    /// </summary>
    public (bool Linked, string? AccountId) CheckAndLinkFromSuperChat(string youtubeChannelId, string message, string displayName)
    {
        // Check if this YouTube channel is already linked
        if (_youtubeIndex.ContainsKey(youtubeChannelId))
        {
            // Already linked, just return the account ID
            return (false, _youtubeIndex[youtubeChannelId]);
        }
        
        // Search for a matching verification code in any account
        foreach (var account in _accounts.Values)
        {
            if (string.IsNullOrEmpty(account.PendingVerificationCode))
                continue;
                
            if (account.VerificationCodeExpiresAt.HasValue && account.VerificationCodeExpiresAt.Value < DateTime.UtcNow)
                continue;
            
            if (message.Contains(account.PendingVerificationCode, StringComparison.OrdinalIgnoreCase))
            {
                // Found a match! Link the channel
                lock (account)
                {
                    account.LinkedYouTubeChannelId = youtubeChannelId;
                    account.PendingVerificationCode = null;
                    account.VerificationCodeExpiresAt = null;
                    
                    // Update display name if not set
                    if (account.DisplayName == account.Username)
                    {
                        account.DisplayName = displayName;
                    }
                }
                
                _youtubeIndex[youtubeChannelId] = account.Id;
                SaveAccounts();
                
                _logger.LogInformation("[ACCOUNT] Linked YouTube channel {ChannelId} to {Username}", 
                    youtubeChannelId, account.Username);
                
                return (true, account.Id);
            }
        }
        
        return (false, null);
    }

    /// <summary>
    /// Directly links a YouTube channel to an account (used for OAuth linking).
    /// </summary>
    public bool LinkYouTubeChannel(string accountId, string youtubeChannelId)
    {
        if (!_accounts.TryGetValue(accountId, out var account))
        {
            return false;
        }
        
        // Check if this YouTube channel is already linked to another account
        if (_youtubeIndex.ContainsKey(youtubeChannelId))
        {
            return false;
        }
        
        lock (account)
        {
            account.LinkedYouTubeChannelId = youtubeChannelId;
            account.PendingVerificationCode = null;
            account.VerificationCodeExpiresAt = null;
        }
        
        _youtubeIndex[youtubeChannelId] = accountId;
        SaveAccounts();
        
        _logger.LogInformation("[ACCOUNT] Linked YouTube channel {ChannelId} to account {AccountId}", 
            youtubeChannelId, accountId);
        
        return true;
    }

    /// <summary>
    /// Gets an account by YouTube Channel ID.
    /// </summary>
    public Account? GetAccountByYouTubeChannel(string youtubeChannelId)
    {
        if (_youtubeIndex.TryGetValue(youtubeChannelId, out var accountId))
        {
            _accounts.TryGetValue(accountId, out var account);
            return account;
        }
        return null;
    }

    /// <summary>
    /// Adds credits to an account.
    /// </summary>
    public void AddCredits(string accountId, decimal amount)
    {
        if (_accounts.TryGetValue(accountId, out var account))
        {
            lock (account)
            {
                account.CreditBalance += amount;
            }
            SaveAccounts();
            _logger.LogInformation("[ACCOUNT] Added ${Amount} to {Username}. Balance: ${Balance}",
                amount, account.Username, account.CreditBalance);
        }
    }

    /// <summary>
    /// Deducts credits from an account.
    /// </summary>
    public bool DeductCredits(string accountId, decimal amount)
    {
        if (!_accounts.TryGetValue(accountId, out var account))
        {
            return false;
        }

        lock (account)
        {
            if (account.CreditBalance < amount)
            {
                return false;
            }

            account.CreditBalance -= amount;
            account.TotalSpent += amount;
            account.LastRequestTime = DateTime.UtcNow;
        }

        SaveAccounts();
        return true;
    }

    /// <summary>
    /// Checks rate limit for an account.
    /// </summary>
    public (bool Allowed, double WaitSeconds) CheckRateLimit(string accountId)
    {
        if (!_accounts.TryGetValue(accountId, out var account))
        {
            return (true, 0);
        }

        var timeSinceLast = DateTime.UtcNow - account.LastRequestTime;
        if (timeSinceLast.TotalSeconds < DEFAULT_RATE_LIMIT_SECONDS)
        {
            return (false, DEFAULT_RATE_LIMIT_SECONDS - timeSinceLast.TotalSeconds);
        }

        return (true, 0);
    }

    private static string HashPassword(string password)
    {
        // Use PBKDF2 with a random salt for secure password hashing
        var salt = new byte[16];
        RandomNumberGenerator.Fill(salt);
        
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256);
        var hash = pbkdf2.GetBytes(32);
        
        // Combine salt + hash for storage
        var combined = new byte[salt.Length + hash.Length];
        Array.Copy(salt, 0, combined, 0, salt.Length);
        Array.Copy(hash, 0, combined, salt.Length, hash.Length);
        
        return Convert.ToBase64String(combined);
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        try
        {
            var combined = Convert.FromBase64String(storedHash);
            if (combined.Length < 48) return false; // 16 (salt) + 32 (hash)
            
            var salt = new byte[16];
            var storedHashBytes = new byte[32];
            Array.Copy(combined, 0, salt, 0, 16);
            Array.Copy(combined, 16, storedHashBytes, 0, 32);
            
            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256);
            var computedHash = pbkdf2.GetBytes(32);
            
            return CryptographicOperations.FixedTimeEquals(computedHash, storedHashBytes);
        }
        catch
        {
            return false;
        }
    }

    private static string GenerateSessionToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static string GenerateRandomCode(int length)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var code = new char[length];
        for (int i = 0; i < length; i++)
        {
            code[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
        }
        return new string(code);
    }

    private void LoadAccounts()
    {
        try
        {
            if (File.Exists(_accountsPath))
            {
                var json = File.ReadAllText(_accountsPath);
                var accounts = JsonSerializer.Deserialize<List<Account>>(json);

                if (accounts != null)
                {
                    foreach (var account in accounts)
                    {
                        _accounts[account.Id] = account;
                        _usernameIndex[account.Username] = account.Id;
                        
                        if (!string.IsNullOrEmpty(account.LinkedYouTubeChannelId))
                        {
                            _youtubeIndex[account.LinkedYouTubeChannelId] = account.Id;
                        }
                        
                        if (!string.IsNullOrEmpty(account.SessionToken) && 
                            account.SessionExpiresAt.HasValue && 
                            account.SessionExpiresAt.Value > DateTime.UtcNow)
                        {
                            _sessionIndex[account.SessionToken] = account.Id;
                        }
                    }
                    _logger.LogInformation("Loaded {Count} accounts from disk", _accounts.Count);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load accounts");
        }
    }

    private void SaveAccounts()
    {
        lock (_lock)
        {
            try
            {
                var json = JsonSerializer.Serialize(_accounts.Values.ToList(), new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_accountsPath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save accounts");
            }
        }
    }
}
