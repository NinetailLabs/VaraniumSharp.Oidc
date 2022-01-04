using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace VaraniumSharp.Oidc.Tests.Logging
{
    public class LogDetails
    {
        #region Properties

        public Exception? Exception { get; set; }

        public string FormattedMessage { get; set; }
        public LogLevel Level { get; set; }

        #endregion
    }
}