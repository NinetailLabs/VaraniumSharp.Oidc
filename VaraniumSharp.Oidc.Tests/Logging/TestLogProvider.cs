using Microsoft.Extensions.Logging;

namespace VaraniumSharp.Oidc.Tests.Logging
{
    public class TestLogProvider : ILoggerProvider
    {
        #region Public Methods

        public ILogger CreateLogger(string categoryName)
        {
            return _logger;
        }

        public void Dispose()
        {
            
        }

        #endregion

        #region Variables

        private readonly ILogger _logger = new TestLogger();

        #endregion
    }
}