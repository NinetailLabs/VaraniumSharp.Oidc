using System.IO;
using System.Net;
using HttpMockSlim.Model;
using Newtonsoft.Json;

namespace VaraniumSharp.Oidc.Tests.Fixtures
{
    public class RefreshTokenHandler
    {
        #region Constructor

        public RefreshTokenHandler(string accessToken, string refreshToken, bool returnError = false)
        {
            _accessToken = accessToken;
            _refreshToken = refreshToken;
            _returnError = returnError;
        }

        #endregion

        #region Properties

        public string TokenPath => "/protocol/openid-connect/token";

        #endregion

        #region Public Methods

        public void Handle(Request request, Response response)
        {
            if (request.Method == "POST")
            {
                if (_returnError)
                {
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    _returnError = false;
                    return;
                }

                var tokenResponse =
                    JsonConvert.SerializeObject(new TokenResponseWrapper(_accessToken, _refreshToken));

                response.ContentType = "application/json";
                response.StatusCode = (int)HttpStatusCode.OK;
                var memStream = new MemoryStream();
                var streamWrite = new StreamWriter(memStream);
                streamWrite.Write(tokenResponse);
                streamWrite.Flush();
                memStream.Position = 0;
                if (response.Body == null)
                {
                    response.Body = memStream;
                }
                else
                {
                    memStream.CopyTo(response.Body);
                }
            }
        }

        #endregion

        #region Variables

        private readonly string _accessToken;

        private readonly string _refreshToken;

        private bool _returnError;

        #endregion
    }
}