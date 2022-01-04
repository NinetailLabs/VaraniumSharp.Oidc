using System;
using Microsoft.Extensions.Logging;

namespace VaraniumSharp.Oidc.Tests.Logging
{
    public class LogDetails
    {
        #region Properties

        public Exception? Exception { get; set; }

        public string FormattedMessage { get; init; }

        public LogLevel Level { get; init; }

        #endregion
    }
}