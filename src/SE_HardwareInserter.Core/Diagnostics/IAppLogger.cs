using System;
using System.Collections.Generic;

namespace SE_HardwareInserter.Core.Diagnostics
{
    /// <summary>Registro estructurado best-effort para operaciones del add-in.</summary>
    public interface IAppLogger
    {
        void Info(
            string operationId,
            string phase,
            string message,
            Exception? exception = null,
            IReadOnlyDictionary<string, string>? context = null);

        void Error(
            string operationId,
            string phase,
            string message,
            Exception? exception = null,
            IReadOnlyDictionary<string, string>? context = null);
    }

    /// <summary>Logger nulo para conservar compatibilidad con constructores existentes.</summary>
    public sealed class NullAppLogger : IAppLogger
    {
        public static NullAppLogger Instance { get; } = new NullAppLogger();

        private NullAppLogger()
        {
        }

        public void Info(
            string operationId,
            string phase,
            string message,
            Exception? exception = null,
            IReadOnlyDictionary<string, string>? context = null)
        {
        }

        public void Error(
            string operationId,
            string phase,
            string message,
            Exception? exception = null,
            IReadOnlyDictionary<string, string>? context = null)
        {
        }
    }
}
