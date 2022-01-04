using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using HttpMockSlim;
using HttpMockSlim.Model;
using Newtonsoft.Json;

namespace VaraniumSharp.Oidc.Tests.Fixtures
{
    public class UserSigninHandler : IHttpHandlerMock
    {
        #region Constructor

        public UserSigninHandler(string callbackUrl, string accessToken, string refreshToken, TokenGenerator generator)
        {
            _callbackUrl = callbackUrl;
            _accessToken = accessToken;
            _refreshToken = refreshToken;
            _generator = generator;
        }

        #endregion

        #region Properties

        public string AuthPath => "/protocol/openid-connect/auth";

        #endregion

        #region Public Methods

        public bool Handle(HttpListenerContext context)
        {
            if (context.Request.HttpMethod == "GET")
            {
                var state = context.Request.QueryString["state"];
                _nonce = context.Request.QueryString["Nonce"] ?? string.Empty;

                _idToken = _generator.GenerateToken(new ClaimsIdentity(new List<Claim>
                {
                    new Claim("sub", "blah"),
                    new Claim("nonce", _nonce)
                }));

                var code = Guid.NewGuid().ToString();
                NonceStorage.Add(code, _nonce);

                using (var client = new HttpClient())
                {
                    client.PostAsync(_callbackUrl, new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
                    {
                        new KeyValuePair<string, string>("access_token", _accessToken),
                        new KeyValuePair<string, string>("refresh_token", _refreshToken),
                        new KeyValuePair<string, string>("code", code),
                        new KeyValuePair<string, string>("state", state),
                        new KeyValuePair<string, string>("id_token", _idToken),
                        new KeyValuePair<string, string>("nonce", _nonce)
                    })).Wait();
                }

                return true;
            }

            if (context.Request.HttpMethod == "POST")
            {
                var responseData = JsonConvert.SerializeObject(new TokenResponseWrapper(_accessToken, _refreshToken)
                {
                    IdentityToken = _idToken
                });

                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.ContentType = "application/json";
                context.Response.SendChunked = true;
                var memStream = new MemoryStream();
                var streamWrite = new StreamWriter(memStream);
                streamWrite.Write(responseData);
                streamWrite.Flush();
                memStream.Position = 0;
                memStream.CopyTo(context.Response.OutputStream);
                context.Response.Close();

                return true;
            }

            return false;
        }
        
        public void HandleTokenExchange(Request request, Response response)
        {
            var streamReader = new StreamReader(request.Body);
            var data = streamReader.ReadToEnd();
            var dataArray = data.Split("&");
            var codeArray = dataArray.First(x => x.StartsWith("code")).Split("=");
            var (code, nonce) = NonceStorage.First(x => x.Key == codeArray[1]);

            _idToken = _generator.GenerateToken(new ClaimsIdentity(new List<Claim>
            {
                new Claim("sub", "blah"),
                new Claim("nonce", nonce)
            }));

            var responseData = new
            {
                access_token = _accessToken,
                refresh_token = _refreshToken,
                code,
                id_token = _idToken,
                nonce
            };

            var jsonResponse = JsonConvert.SerializeObject(responseData);
            response.ContentType = "application/json";
            response.StatusCode = (int)HttpStatusCode.OK;
            var memStream = new MemoryStream();
            var streamWrite = new StreamWriter(memStream);
            streamWrite.Write(jsonResponse);
            streamWrite.Flush();
            memStream.Position = 0;
            memStream.CopyTo(response.Body);
        }

        #endregion

        #region Variables

        /// <summary>
        /// Dictionary used to store Code and Nonce values
        /// </summary>
        private static readonly Dictionary<string, string> NonceStorage = new Dictionary<string, string>();

        private readonly string _accessToken;

        private readonly string _callbackUrl;

        private readonly TokenGenerator _generator;

        private readonly string _refreshToken;

        private string _idToken;

        private string _nonce;

        #endregion
    }
}