﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IdentityModel.Client;
using IdentityModel.OidcClient;
using Microsoft.Extensions.Logging;
using VaraniumSharp.Attributes;
using VaraniumSharp.Enumerations;
using VaraniumSharp.Interfaces.GenericHelpers;
using VaraniumSharp.Oidc.Interfaces;

namespace VaraniumSharp.Oidc
{
    /// <summary>
    /// Manage Access Tokens
    /// </summary>
    [AutomaticContainerRegistration(typeof(ITokenManager), ServiceReuse.Singleton)]
    public class TokenManager : ITokenManager
    {
        #region Constructor

        /// <summary>
        /// DI Constructor
        /// </summary>
        /// <param name="tokenStorage">TokenStorage implementation</param>
        /// <param name="staticMethodWrapper">StaticMethodWrapper instance</param>
        public TokenManager(ITokenStorage tokenStorage, IStaticMethodWrapper staticMethodWrapper)
        {
            _tokenStorage = tokenStorage;
            _staticMethodWrapper = staticMethodWrapper;

            _tokenDictionary = new Dictionary<string, TokenData>();
            _refreshDictionary = new Dictionary<string, string>();
            _tokenLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
            _serverDetails = new Dictionary<string, IdentityServerConnectionDetails>();
            _tokenRefreshTimers = new Dictionary<string, Timer>();
            RefreshTimeSpan = TimeSpan.FromHours(1);
            _log = Logging.StaticLogger.GetLogger<TokenManager>();
        }

        #endregion

        #region Events

        /// <inheritdoc />
        public event EventHandler<KeyValuePair<string, TokenData>> TokenRefreshed;

        #endregion

        #region Properties

        /// <inheritdoc />
        public TimeSpan RefreshTimeSpan { get; private set; }

        /// <summary>
        /// Get list of TokenNames that have Populated server details
        /// </summary>
        public List<string> ServerDetailKeys => _serverDetails.Keys.ToList();

        #endregion

        #region Public Methods

        /// <summary>
        /// Add connection details for an Identity Server that is associated with a specific token.
        /// If the same tokenName is passed again the IdentityServerConnectionDetails will be updated
        /// <remarks>
        /// This method must be called before attempting to validate tokens
        /// </remarks>
        /// </summary>
        /// <param name="tokenName">Name of the token that will be retrieved with the details</param>
        /// <param name="connectionDetails">Details that is used to identify the client to the Identity Server</param>
        public async Task AddServerDetails(string tokenName, IdentityServerConnectionDetails connectionDetails)
        {
            var semaphore = _tokenLocks.GetOrAdd(tokenName, new SemaphoreSlim(1));
            try
            {
                await semaphore.WaitAsync();

                if (_serverDetails.ContainsKey(tokenName))
                {
                    _serverDetails[tokenName] = connectionDetails;
                }
                else
                {
                    _serverDetails.Add(tokenName, connectionDetails);
                }
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// Retrieve the user's Access Token.
        /// This method executes all the necessary steps to retrieve the Access Token, validate it's expiry, handle refresh (if required) or all else failing guiding the user through login
        /// </summary>
        /// <param name="tokenName">Name of the token</param>
        /// <param name="extraParameters">Additional parameters to pass to the OidcClient</param>
        /// <exception cref="ArgumentException">Thrown if the ServerDetails for the specific tokenName has not been populated</exception>
        /// <returns>TokenData if the user has an Access Token, otherwise null</returns>
        public async Task<TokenData> CheckSigninAsync(string tokenName,
            Dictionary<string, string> extraParameters = null)
        {
            var semaphore = _tokenLocks.GetOrAdd(tokenName, new SemaphoreSlim(1));
            try
            {
                await semaphore.WaitAsync();
                if (!_serverDetails.ContainsKey(tokenName))
                {
                    throw new ArgumentException(
                        $"{tokenName} does not have server data yet, please call AddServerDetails with the same token name before attempting to sign in");
                }

                var tokenData = (await RetrieveAccessToken(tokenName)
                                 ?? await RefreshTokenAsync(tokenName))
                                ?? await AuthenticateClient(tokenName, extraParameters);

                return tokenData;
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <inheritdoc />
        public void SetupRefreshTimeSpan(TimeSpan refreshTimeSpan)
        {
            lock (_refreshLock)
            {
                RefreshTimeSpan = refreshTimeSpan;
                var keys = _tokenRefreshTimers.Keys.ToList();
                foreach (var key in keys)
                {
                    var accessToken = _tokenDictionary[key];
                    var timeTillExpiration = accessToken.ExpirationDate - DateTime.UtcNow;
                    if (timeTillExpiration > refreshTimeSpan)
                    {
                        SetupRefreshTokenTimer(key, accessToken);
                    }
                    else
                    {
                        _tokenRefreshTimers[key].Dispose();
                        _tokenRefreshTimers.Remove(key);
                        TokenExpirationCallback(key);
                    }
                }
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Guide the user through the sign-in process in order to acquire an Access Token
        /// </summary>
        /// <param name="tokenName">Name of the token</param>
        /// <param name="extraParameters">Additional parameters to pass to the OidcClient</param>
        /// <returns>Access Token if successful, otherwise null</returns>
        private async Task<TokenData> AuthenticateClient(string tokenName, Dictionary<string, string> extraParameters)
        {
            TokenData token;

            var options = _serverDetails[tokenName];
            // create an HttpListener to listen for requests on that redirect URI.
            var http = new HttpListener();
            try
            {
                http.Prefixes.Add(options.OidcOptions.RedirectUri);
                http.Start();

                var client = new OidcClient(options.OidcOptions);
                var state = extraParameters == null
                    ? await client.PrepareLoginAsync()
                    : await client.PrepareLoginAsync(new Parameters(extraParameters));

                _staticMethodWrapper.StartProcess(state.StartUrl);

                var context = await http.GetContextAsync();

                var formData = GetRequestPostData(context.Request);
                var response = context.Response;
                var responseString = string.IsNullOrEmpty(options.ReturnToClientHtml)
                    ? "<html><body>Please return to the app.</body></html>"
                    : options.ReturnToClientHtml;
                var buffer = Encoding.UTF8.GetBytes(responseString);
                response.ContentLength64 = buffer.Length;
                var responseOutput = response.OutputStream;
                await responseOutput.WriteAsync(buffer, 0, buffer.Length);
                responseOutput.Close();

                var result = await client.ProcessResponseAsync(formData, state);
                if (result.IsError)
                {
                    _log.LogError("Error occurred during user authentication. {Error}", result.Error);
                    return null;
                }
                else
                {
                    await UpdateRefreshTokenStorage(tokenName, result.RefreshToken);
                    token = await UpdateAccessTokenStorage(tokenName, result.AccessToken);
                }
            }
            finally
            {
                http.Stop();
            }

            return token;
        }

        /// <summary>
        /// Execute an Access token refresh, updating the data stores.
        /// <remarks>
        /// This method does not lock the tokenName semaphore, it is the responsibility of callers to ensure the semaphore is locked.
        /// </remarks>
        /// </summary>
        /// <param name="tokenName">Name of the token</param>
        /// <param name="refreshToken">The refresh token to use</param>
        /// <param name="replaceRefreshToken">Should the refresh token be replaced. This is required in case the server gives a new refresh token when the current one is used</param>
        /// <param name="options">Options for connecting to the Identity Server</param>
        /// <returns>New token unless refresh could not be carried out, then null</returns>
        private async Task<TokenData> ExecuteTokenRefreshAsync(string tokenName, string refreshToken,
            bool replaceRefreshToken, OidcClientOptions options)
        {
            var client = new OidcClient(options);
            var result = await client.RefreshTokenAsync(refreshToken);
            if (result.IsError)
            {
                _log.LogError("Error occurred while trying to refresh Access Token. {Error}", result.Error);
                return null;
            }

            // Save our tokens to the datastore
            if (replaceRefreshToken)
            {
                await UpdateRefreshTokenStorage(tokenName, result.RefreshToken);
            }

            return await UpdateAccessTokenStorage(tokenName, result.AccessToken);
        }

        private static string GetRequestPostData(HttpListenerRequest request)
        {
            if (!request.HasEntityBody)
            {
                return null;
            }

            using (var body = request.InputStream)
            {
                using (var reader = new System.IO.StreamReader(body, request.ContentEncoding))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        /// <summary>
        /// Retrieve refresh token from storage and use it to attempt to refresh the Access Token
        /// </summary>
        /// <param name="tokenName">Name of the token</param>
        /// <returns>Fresh Access Token unless refresh failed in which case null</returns>
        private async Task<TokenData> RefreshTokenAsync(string tokenName)
        {
            _refreshDictionary.TryGetValue(tokenName, out var rToken);

            if (string.IsNullOrEmpty(rToken))
            {
                rToken = await _tokenStorage.RetrieveRefreshTokenAsync(tokenName);
            }

            // We do not have a refresh token
            if (string.IsNullOrEmpty(rToken))
            {
                return null;
            }

            _refreshDictionary.Add(tokenName, rToken);

            // We need to call out to have our Access Token refreshed
            var connectionDetails = _serverDetails[tokenName];
            return await ExecuteTokenRefreshAsync(tokenName, rToken, connectionDetails.ReplaceRefreshToken,
                connectionDetails.OidcOptions);
        }

        /// <summary>
        /// Retrieve token from storage and validate that it has not expired yet
        /// </summary>
        /// <param name="tokenName">Name of the token to retrieve</param>
        /// <returns>TokenData unless the token does not exist or has expired in which case null is returned</returns>
        private async Task<TokenData> RetrieveAccessToken(string tokenName)
        {
            _tokenDictionary.TryGetValue(tokenName, out var dToken);

            if (dToken == null)
            {
                dToken = await _tokenStorage.RetrieveAccessTokenAsync(tokenName);
            }

            if (dToken != null && !_tokenDictionary.ContainsKey(tokenName))
            {
                _tokenDictionary.Add(tokenName, dToken);
            }

            if (dToken == null
                || dToken.TokenExpired)
            {
                return null;
            }

            var timeTillExpiration = dToken.ExpirationDate - DateTime.UtcNow;
            SetupRefreshTokenTimer(tokenName, dToken);

            return timeTillExpiration <= RefreshTimeSpan
                ? null
                : dToken;
        }

        /// <summary>
        /// Set up a timer that will refresh an access token an hour before it expires. 
        /// </summary>
        /// <param name="tokenName">Name of the token</param>
        /// <param name="accessTokenData">The current access token</param>
        private void SetupRefreshTokenTimer(string tokenName, TokenData accessTokenData)
        {
            var timeTillExpiration = accessTokenData.ExpirationDate - DateTime.UtcNow;
            if (timeTillExpiration <= RefreshTimeSpan)
            {
                return;
            }

            lock (_refreshLock)
            {
                if (_tokenRefreshTimers.ContainsKey(tokenName))
                {
                    _tokenRefreshTimers[tokenName].Dispose();
                    _tokenRefreshTimers.Remove(tokenName);
                }

                _tokenRefreshTimers.Add(tokenName,
                    new Timer(TokenExpirationCallback, tokenName, timeTillExpiration.Subtract(RefreshTimeSpan),
                        TimeSpan.FromMilliseconds(-1)));
            }
        }

        /// <summary>
        /// Fired when an access token is an hour from expiration.
        /// Will refresh the token, reset the timer and notify listeners of the token update
        /// </summary>
        /// <param name="state">Name of the token that expired</param>
        private async void TokenExpirationCallback(object state)
        {
            var tokenName = state.ToString();
            _log.LogDebug("Refreshing token {TokenName} as it will expire in {ExpirationTimeout}", tokenName,
                RefreshTimeSpan);
            var token = await RefreshTokenAsync(tokenName);
            if (token != null)
            {
                SetupRefreshTokenTimer(tokenName, token);
                TokenRefreshed?.Invoke(this, new KeyValuePair<string, TokenData>(tokenName, token));
            }
            else
            {
                _log.LogWarning("Attempting to refresh access token failed. No further auto-refreshes will occur for {TokenName}", tokenName);
            }
        }

        /// <summary>
        /// Store the Access token in the datastore and add/update the <see cref="_tokenDictionary"/>
        /// </summary>
        /// <param name="tokenName">Name of the token</param>
        /// <param name="newToken">Access token string to store</param>
        /// <returns>Populated TokenData from the access token</returns>
        private async Task<TokenData> UpdateAccessTokenStorage(string tokenName, string newToken)
        {
            await _tokenStorage.StoreAccessTokenAsync(tokenName, newToken);
            var tokenData = new TokenData(newToken);
            if (_tokenDictionary.ContainsKey(tokenName))
            {
                _tokenDictionary[tokenName] = tokenData;
            }
            else
            {
                _tokenDictionary.Add(tokenName, tokenData);
            }

            return tokenData;
        }

        /// <summary>
        /// Store refresh token in the datastore and add/update the <see cref="_refreshDictionary"/>
        /// </summary>
        /// <param name="tokenName">Name of the token</param>
        /// <param name="newRefreshToken">Refresh token that should be stored</param>
        private async Task UpdateRefreshTokenStorage(string tokenName, string newRefreshToken)
        {
            await _tokenStorage.StoreRefreshTokenAsync(tokenName, newRefreshToken);
            if (_refreshDictionary.ContainsKey(tokenName))
            {
                _refreshDictionary[tokenName] = newRefreshToken;
            }
            else
            {
                _refreshDictionary.Add(tokenName, newRefreshToken);
            }
        }

        #endregion

        #region Variables

        /// <summary>
        /// Logger instance
        /// </summary>
        private readonly ILogger _log;

        /// <summary>
        /// Dictionary to store refresh tokens
        /// </summary>
        private readonly Dictionary<string, string> _refreshDictionary;

        /// <summary>
        /// Object used to lock access to the <see cref="_tokenRefreshTimers"/> dictionary
        /// </summary>
        private readonly object _refreshLock = new object();

        /// <summary>
        /// Dictionary to store server connection details
        /// </summary>
        private readonly Dictionary<string, IdentityServerConnectionDetails> _serverDetails;

        /// <summary>
        /// StaticMethodWrapper instance
        /// </summary>
        private readonly IStaticMethodWrapper _staticMethodWrapper;

        /// <summary>
        /// Dictionary to store token data
        /// </summary>
        private readonly Dictionary<string, TokenData> _tokenDictionary;

        /// <summary>
        /// Semaphores used to lock token access
        /// </summary>
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _tokenLocks;

        /// <summary>
        /// Timers used to request new Access tokens before they expire
        /// </summary>
        private readonly Dictionary<string, Timer> _tokenRefreshTimers;

        /// <summary>
        /// Token storage instance
        /// </summary>
        private readonly ITokenStorage _tokenStorage;

        #endregion
    }
}