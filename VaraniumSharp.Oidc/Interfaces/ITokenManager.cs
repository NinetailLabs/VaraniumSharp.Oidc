﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VaraniumSharp.Oidc.Models;

namespace VaraniumSharp.Oidc.Interfaces
{
    /// <summary>
    /// Manage Access Tokens
    /// </summary>
    public interface ITokenManager
    {
        #region Events

        /// <summary>
        /// Fired when the Access token has been refreshed.
        /// Provides the name of the token as well as the new Access Token
        /// </summary>
        event EventHandler<KeyValuePair<string, TokenData>> TokenRefreshed;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the TimeSpan before a token expires when it should be refresh.
        /// Default to 1 hour
        /// </summary>
        TimeSpan RefreshTimeSpan { get; }

        /// <summary>
        /// Get list of TokenNames that have Populated server details
        /// </summary>
        List<string> ServerDetailKeys { get; }

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
        Task AddServerDetails(string tokenName, IdentityServerConnectionDetails connectionDetails);

        /// <summary>
        /// Retrieve the user's Access Token.
        /// This method executes all the necessary steps to retrieve the Access Token, validate it's expiry, handle refresh (if required) or all else failing guiding the user through login
        /// </summary>
        /// <param name="tokenName">Name of the token</param>
        /// <param name="extraParameters">Additional parameters to pass to the OidcClient</param>
        /// <exception cref="ArgumentException">Thrown if the ServerDetails for the specific tokenName has not been populated</exception>
        /// <returns>TokenData if the user has an Access Token, otherwise null</returns>
        Task<TokenData> CheckSigninAsync(string tokenName, Dictionary<string, string> extraParameters = null);

        /// <summary>
        /// Sets the TimeSpan used to determine if a valid token should be refreshed.
        /// If TimeSpan of 1 hour is provided, an Access Token will be refreshed 1 hour before it is set to expire
        /// </summary>
        /// <param name="refreshTimeSpan">TimeSpan for the refresh</param>
        void SetupRefreshTimeSpan(TimeSpan refreshTimeSpan);

        #endregion
    }
}