using System;
using Microsoft.Extensions.Logging;

namespace VaraniumSharp.Oidc.Tests.Logging
{
    public class LogDetails
    {
        public LogLevel Level { get; set; }

        public Exception? Exception { get; set; }


    }
}