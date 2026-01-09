using Azure.Core;
using Azure.Identity;
using Microsoft.Identity.Client;

namespace BlobStorageDriver.CloudProvider;

/// <summary>
/// Provides MSAL-based token acquisition with in-memory caching.
/// Tokens acquired via the embedded sign-in window are stored here and reused.
/// </summary>
public static class MsalTokenProvider
{
    // Azure CLI client ID - well-known public client
    public const string ClientId = "04b07795-8ddb-461a-bbee-02f9e1bf7b46";
    
    // Storage scope for Azure Blob Storage
    private static readonly string[] StorageScopes = new[] { "https://storage.azure.com/.default" };
    
    // Cached authentication result
    private static AuthenticationResult? _cachedResult;
    private static IPublicClientApplication? _app;
    private static readonly object _lock = new();
    
    /// <summary>
    /// Gets the shared MSAL public client application
    /// </summary>
    public static IPublicClientApplication GetApp(string? tenantId = null)
    {
        lock (_lock)
        {
            if (_app == null)
            {
                var builder = PublicClientApplicationBuilder
                    .Create(ClientId)
                    .WithAuthority(AzureCloudInstance.AzurePublic, tenantId ?? "common")
                    .WithDefaultRedirectUri();
                
                _app = builder.Build();
            }
            return _app;
        }
    }
    
    /// <summary>
    /// Stores the authentication result from interactive sign-in
    /// </summary>
    public static void SetCachedToken(AuthenticationResult result)
    {
        lock (_lock)
        {
            _cachedResult = result;
        }
    }
    
    /// <summary>
    /// Gets the cached token if available and not expired
    /// </summary>
    public static AuthenticationResult? GetCachedToken()
    {
        lock (_lock)
        {
            if (_cachedResult != null && _cachedResult.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                return _cachedResult;
            }
            return null;
        }
    }
    
    /// <summary>
    /// Checks if we have a valid cached token
    /// </summary>
    public static bool HasValidToken => GetCachedToken() != null;
    
    /// <summary>
    /// Gets an access token, using cache first, then silent acquisition
    /// </summary>
    public static async Task<string?> GetAccessTokenAsync(string? tenantId = null, CancellationToken cancellationToken = default)
    {
        // Check in-memory cache first
        var cached = GetCachedToken();
        if (cached != null)
        {
            return cached.AccessToken;
        }
        
        // Try silent acquisition from MSAL cache
        try
        {
            var app = GetApp(tenantId);
            var accounts = await app.GetAccountsAsync();
            var account = accounts.FirstOrDefault();
            
            if (account != null)
            {
                var result = await app.AcquireTokenSilent(StorageScopes, account)
                    .ExecuteAsync(cancellationToken);
                SetCachedToken(result);
                return result.AccessToken;
            }
        }
        catch
        {
            // Silent acquisition failed - no cached token available
        }
        
        return null;
    }
    
    /// <summary>
    /// Clears the cached token
    /// </summary>
    public static void ClearCache()
    {
        lock (_lock)
        {
            _cachedResult = null;
        }
    }
}

/// <summary>
/// Azure.Core TokenCredential that uses the MSAL cached token
/// </summary>
public class MsalCachedTokenCredential : TokenCredential
{
    private readonly string? _tenantId;
    
    public MsalCachedTokenCredential(string? tenantId = null)
    {
        _tenantId = tenantId;
    }
    
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        return GetTokenAsync(requestContext, cancellationToken).GetAwaiter().GetResult();
    }
    
    public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        var token = await MsalTokenProvider.GetAccessTokenAsync(_tenantId, cancellationToken);
        
        if (token == null)
        {
            throw new CredentialUnavailableException("No cached MSAL token available. Please sign in first using the 'Sign In with Microsoft' button.");
        }
        
        var cached = MsalTokenProvider.GetCachedToken();
        return new AccessToken(token, cached?.ExpiresOn ?? DateTimeOffset.UtcNow.AddHours(1));
    }
}
