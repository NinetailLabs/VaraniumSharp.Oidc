using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace VaraniumSharp.Oidc.Tests.Logging
{
    public class TestLogger : ILogger
    {
        #region Properties

        public List<LogDetails> LogEntries { get; } = new();

        #endregion

        #region Public Methods

        public IDisposable BeginScope<TState>(TState state)
        {
            throw new NotImplementedException();
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            LogEntries.Add(new LogDetails
            {
                Level = logLevel,
                Exception = exception
            });
            throw new NotImplementedException();
        }

        #endregion
    }
}