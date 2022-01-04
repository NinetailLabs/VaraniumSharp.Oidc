using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using HttpMockSlim.Model;
using IdentityModel.Client;
using Newtonsoft.Json;

namespace VaraniumSharp.Oidc.Tests.Fixtures
{
    public class UserInfoFixture
    {
        #region Properties

        public string UserInfoPath => "/protocol/openid-connect/userinfo";

        #endregion

        #region Public Methods

        public bool Handle(Request request, Response response)
        {
            if (request.Method == "GET")
            {
                //So this is a little weird, but it is apparently the way it expects the claims so that's how we do it
                var claimCollection = new UserInfoData
                {
                    Sub = "blah"
                };
                var userInfoJson = JsonConvert.SerializeObject(claimCollection);
                var userInfo = new UserInfoResponseFixture();
                userInfo.InitAsync(userInfoJson).Wait();

                response.ContentType = "application/json";
                response.StatusCode = (int)HttpStatusCode.OK;
                var memStream = new MemoryStream();
                var streamWrite = new StreamWriter(memStream);
                streamWrite.Write(userInfo.Json);
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
                return true;
            }

            return false;
        }

        #endregion

        private class UserInfoResponseFixture : UserInfoResponse
        {
            #region Public Methods

            public async Task InitAsync(string json)
            {
                var doc = JsonDocument.Parse(json);
                Json = doc.RootElement;
                //Json = JsonConvert.DeserializeObject<JsonElement>(json);
                await InitializeAsync(json);
            }

            #endregion
        }

        private class UserInfoData
        {
            #region Properties

            [JsonProperty("sub")]
            public string Sub { get; set; }

            #endregion
        }
    }
}