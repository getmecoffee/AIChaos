using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using AIChaos.Brain.Models;

namespace AIChaos.Brain.Services;

/// <summary>
/// Service for managing users, credits, and rate limits.
/// </summary>
public class UserService
{
    private readonly string _usersPath;
    private readonly ILogger<UserService> _logger;
    private readonly ConcurrentDictionary<string, User> _users = new();
    private readonly ConcurrentDictionary<string, PendingVerification> _pendingVerifications = new();
    private readonly object _lock = new();

    // Configurable settings
    private const int DEFAULT_RATE_LIMIT_SECONDS = 20;
    private const int VERIFICATION_EXPIRY_MINUTES = 30;

    public UserService(ILogger<UserService> logger)
    {
        _logger = logger;
        _usersPath = Path.Combine(AppContext.BaseDirectory, "users.json");
        LoadUsers();
    }

    /// <summary>
    /// Gets a user by ID, creating them if they don't exist.
    /// </summary>
    public User GetOrCreateUser(string userId, string displayName, string platform = "youtube")
    {
        return _users.GetOrAdd(userId, id =>
        {
            var user = new User
            {
                Id = id,
                DisplayName = displayName,
                Platform = platform,
                CreditBalance = 0
            };
            SaveUsers();
            return user;
        });
    }

    /// <summary>
    /// Gets a user by ID if they exist.
    /// </summary>
    public User? GetUser(string userId)
    {
        if (_users.TryGetValue(userId, out var user))
        {
            return user;
        }
        return null;
    }

    /// <summary>
    /// Adds credits to a user's balance.
    /// </summary>
    public void AddCredits(string userId, decimal amount, string displayName)
    {
        var user = GetOrCreateUser(userId, displayName);

        lock (user)
        {
            user.CreditBalance += amount;
            user.DisplayName = displayName;
        }

        SaveUsers();
        _logger.LogInformation("[USER] Added ${Amount} to {User} ({Id}). New Balance: ${Balance}",
            amount, displayName, userId, user.CreditBalance);
    }
    
    /// <summary>
    /// Generates a verification code for a Channel ID.
    /// User must send a Super Chat containing this code to verify ownership.
    /// </summary>
    public string GenerateVerificationCode(string channelId)
    {
        // Clean up expired verifications
        CleanupExpiredVerifications();
        
        // Check if there's already a pending verification for this channel
        if (_pendingVerifications.TryGetValue(channelId, out var existing) && existing.ExpiresAt > DateTime.UtcNow)
        {
            return existing.VerificationCode;
        }
        
        // Generate new code
        var code = "VERIFY-" + GenerateRandomCode(4);
        var verification = new PendingVerification
        {
            ChannelId = channelId,
            VerificationCode = code,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(VERIFICATION_EXPIRY_MINUTES)
        };
        
        _pendingVerifications[channelId] = verification;
        _logger.LogInformation("[VERIFY] Generated code {Code} for Channel {ChannelId}", code, channelId);
        
        return code;
    }
    
    /// <summary>
    /// Checks if a Super Chat message contains a valid verification code.
    /// Called by YouTubeService when processing Super Chats.
    /// </summary>
    public bool CheckAndVerifyFromSuperChat(string channelId, string message)
    {
        if (!_pendingVerifications.TryGetValue(channelId, out var verification))
        {
            return false;
        }
        
        if (verification.ExpiresAt < DateTime.UtcNow)
        {
            _pendingVerifications.TryRemove(channelId, out _);
            return false;
        }
        
        // Check if message contains the verification code (case-insensitive)
        if (message.Contains(verification.VerificationCode, StringComparison.OrdinalIgnoreCase))
        {
            // Verify the user!
            var user = GetOrCreateUser(channelId, "Verified User");
            lock (user)
            {
                user.IsVerified = true;
            }
            SaveUsers();
            
            // Remove the pending verification
            _pendingVerifications.TryRemove(channelId, out _);
            
            _logger.LogInformation("[VERIFY] Channel {ChannelId} verified via Super Chat!", channelId);
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Checks if a user is verified.
    /// </summary>
    public bool IsUserVerified(string userId)
    {
        if (_users.TryGetValue(userId, out var user))
        {
            return user.IsVerified;
        }
        return false;
    }
    
    /// <summary>
    /// Gets verification status for a channel ID.
    /// </summary>
    public (bool HasCredits, bool IsVerified, decimal Balance, string? PendingCode) GetVerificationStatus(string channelId)
    {
        var user = GetUser(channelId);
        string? pendingCode = null;
        
        if (_pendingVerifications.TryGetValue(channelId, out var verification) && verification.ExpiresAt > DateTime.UtcNow)
        {
            pendingCode = verification.VerificationCode;
        }
        
        return (
            HasCredits: user != null && user.CreditBalance > 0,
            IsVerified: user?.IsVerified ?? false,
            Balance: user?.CreditBalance ?? 0,
            PendingCode: pendingCode
        );
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
    
    private void CleanupExpiredVerifications()
    {
        var expired = _pendingVerifications
            .Where(kv => kv.Value.ExpiresAt < DateTime.UtcNow)
            .Select(kv => kv.Key)
            .ToList();
            
        foreach (var key in expired)
        {
            _pendingVerifications.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Deducts credits from a user's balance.
    /// Returns true if successful, false if insufficient funds.
    /// </summary>
    public bool DeductCredits(string userId, decimal amount)
    {
        if (!_users.TryGetValue(userId, out var user))
        {
            return false;
        }

        lock (user)
        {
            if (user.CreditBalance < amount)
            {
                return false;
            }

            user.CreditBalance -= amount;
            user.TotalSpent += amount;
            user.LastRequestTime = DateTime.UtcNow;
        }

        SaveUsers();
        _logger.LogInformation("[USER] Deducted ${Amount} from {User} ({Id}). New Balance: ${Balance}",
            amount, user.DisplayName, userId, user.CreditBalance);

        return true;
    }

    /// <summary>
    /// Checks if a user can submit a request based on rate limits.
    /// </summary>
    public (bool Allowed, double WaitSeconds) CheckRateLimit(string userId)
    {
        if (!_users.TryGetValue(userId, out var user))
        {
            return (true, 0);
        }

        var timeSinceLast = DateTime.UtcNow - user.LastRequestTime;
        if (timeSinceLast.TotalSeconds < DEFAULT_RATE_LIMIT_SECONDS)
        {
            return (false, DEFAULT_RATE_LIMIT_SECONDS - timeSinceLast.TotalSeconds);
        }

        return (true, 0);
    }

    private void LoadUsers()
    {
        try
        {
            if (File.Exists(_usersPath))
            {
                var json = File.ReadAllText(_usersPath);
                var users = JsonSerializer.Deserialize<List<User>>(json);

                if (users != null)
                {
                    foreach (var user in users)
                    {
                        _users.TryAdd(user.Id, user);
                    }
                    _logger.LogInformation("Loaded {Count} users from disk", _users.Count);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load users");
        }
    }

    private void SaveUsers()
    {
        lock (_lock)
        {
            try
            {
                var json = JsonSerializer.Serialize(_users.Values, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_usersPath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save users");
            }
        }
    }
}
