using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SE_HardwareInserter.Core.Diagnostics
{
    /// <summary>Logger de texto con rotación, retención y saneamiento de datos sensibles.</summary>
    public sealed class FileAppLogger : IAppLogger
    {
        private const long DefaultMaximumBytes = 5L * 1024L * 1024L;
        private const int MessageMaximumCharacters = 16 * 1024;
        private const int ContextValueMaximumCharacters = 4 * 1024;
        private const int IdentifierMaximumCharacters = 256;
        private const string TruncatedMarker = " [TRUNCATED]";
        private static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(14);
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
        private static readonly Regex SensitiveAssignmentRegex = new Regex(
            @"(\b(?:password|token|secret|apikey|authorization)\b\s*[:=]\s*)[^\s&;]+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RegexTimeout);
        private static readonly Regex UriUserInfoRegex = new Regex(
            @"([a-z][a-z0-9+.-]*://)[^/@\s]+@",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RegexTimeout);
        private static readonly Regex SensitiveQueryRegex = new Regex(
            @"([?&](?:password|token|secret|apikey|authorization)=)[^&#\s]+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RegexTimeout);
        private readonly object _sync = new object();
        private readonly string _directoryPath;
        private readonly long _maximumBytes;
        private readonly TimeSpan _retention;
        private readonly Func<DateTime> _utcNow;

        public FileAppLogger()
            : this(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SE_HardwareInserter",
                "logs"))
        {
        }

        public FileAppLogger(
            string directoryPath,
            long maximumBytes = DefaultMaximumBytes,
            TimeSpan? retention = null,
            Func<DateTime>? utcNow = null)
        {
            _directoryPath = directoryPath ?? throw new ArgumentNullException(nameof(directoryPath));
            _maximumBytes = maximumBytes > 0
                ? maximumBytes
                : throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            _retention = retention ?? DefaultRetention;
            if (_retention <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retention));
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public void Info(
            string operationId,
            string phase,
            string message,
            Exception? exception = null,
            IReadOnlyDictionary<string, string>? context = null) =>
            Write("INFO", operationId, phase, message, exception, context);

        public void Error(
            string operationId,
            string phase,
            string message,
            Exception? exception = null,
            IReadOnlyDictionary<string, string>? context = null) =>
            Write("ERROR", operationId, phase, message, exception, context);

        private void Write(
            string level,
            string operationId,
            string phase,
            string message,
            Exception? exception,
            IReadOnlyDictionary<string, string>? context)
        {
            try
            {
                lock (_sync)
                {
                    var now = _utcNow().ToUniversalTime();
                    Directory.CreateDirectory(_directoryPath);
                    ApplyRetention(now);

                    var line = FitLine(BuildLine(now, level, operationId, phase, message, exception, context));
                    const string lineEnding = "\n";
                    var activePath = Path.Combine(
                        _directoryPath,
                        "SE_HardwareInserter-" + now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
                    RotateIfNeeded(activePath, Encoding.UTF8.GetByteCount(line + lineEnding));
                    File.AppendAllText(activePath, line + lineEnding, new UTF8Encoding(false));
                }
            }
            catch (Exception)
            {
                // El diagnóstico nunca puede alterar el comportamiento del add-in.
            }
        }

        private void ApplyRetention(DateTime now)
        {
            foreach (var path in Directory.EnumerateFiles(_directoryPath, "SE_HardwareInserter-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(path) < now - _retention) File.Delete(path);
                }
                catch (Exception)
                {
                    // Un archivo bloqueado no impide escribir el registro actual.
                }
            }
        }

        private void RotateIfNeeded(string activePath, int incomingBytes)
        {
            if (!File.Exists(activePath)) return;
            if (new FileInfo(activePath).Length + incomingBytes <= _maximumBytes) return;

            var stem = Path.GetFileNameWithoutExtension(activePath);
            var suffix = 1;
            var rotatedPath = Path.Combine(_directoryPath, stem + "-001.log");
            while (File.Exists(rotatedPath))
            {
                rotatedPath = Path.Combine(
                    _directoryPath,
                    stem + "-" + suffix.ToString("000", CultureInfo.InvariantCulture) + ".log");
                suffix++;
            }
            File.Move(activePath, rotatedPath);
        }

        private string FitLine(string line)
        {
            const string lineEnding = "\n";
            const string marker = " [TRUNCATED]";
            var budget = checked((int)Math.Min(int.MaxValue, _maximumBytes))
                - Encoding.UTF8.GetByteCount(lineEnding);
            if (budget <= 0) return string.Empty;
            if (Encoding.UTF8.GetByteCount(line) <= budget) return line;

            var markerBytes = Encoding.UTF8.GetByteCount(marker);
            if (markerBytes >= budget) return TruncateUtf8(marker, budget);
            return TruncateUtf8(line, budget - markerBytes) + marker;
        }

        private static string TruncateUtf8(string value, int maximumBytes)
        {
            if (maximumBytes <= 0) return string.Empty;
            var bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length <= maximumBytes) return value;

            var length = maximumBytes;
            var strictUtf8 = new UTF8Encoding(false, true);
            while (length > 0)
            {
                try
                {
                    return strictUtf8.GetString(bytes, 0, length);
                }
                catch (DecoderFallbackException)
                {
                    length--;
                }
            }
            return string.Empty;
        }

        private static string BuildLine(
            DateTime now,
            string level,
            string operationId,
            string phase,
            string message,
            Exception? exception,
            IReadOnlyDictionary<string, string>? context)
        {
            var builder = new StringBuilder();
            builder.Append(now.ToString("O", CultureInfo.InvariantCulture));
            builder.Append('\t').Append(level);
            builder.Append('\t').Append(Normalize(LimitInput(operationId, IdentifierMaximumCharacters)));
            builder.Append('\t').Append(Normalize(LimitInput(phase, IdentifierMaximumCharacters)));
            builder.Append('\t').Append(Sanitize(message, MessageMaximumCharacters));
            if (exception != null)
            {
                builder.Append("\texception=").Append(Normalize(
                    LimitInput(exception.GetType().FullName, IdentifierMaximumCharacters)));
                builder.Append("\thresult=").Append(exception.HResult.ToString(CultureInfo.InvariantCulture));
                builder.Append("\terror=").Append(Sanitize(exception.Message, MessageMaximumCharacters));
            }
            if (context != null)
            {
                foreach (var entry in context.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var key = Normalize(LimitInput(entry.Key, IdentifierMaximumCharacters));
                    var value = IsSensitiveKey(key)
                        ? "[REDACTED]"
                        : Sanitize(entry.Value, ContextValueMaximumCharacters);
                    builder.Append('\t').Append(key).Append('=').Append(value);
                }
            }
            return builder.ToString();
        }

        private static bool IsSensitiveKey(string key)
        {
            var normalized = key.Replace("-", string.Empty).Replace("_", string.Empty);
            return normalized.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("apikey", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("authorization", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("cookie", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("credential", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Normalize(string? value)
        {
            if (value == null || value.Length == 0) return string.Empty;
            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                builder.Append(character == '\r' || character == '\n' || character == '\t'
                    ? ' '
                    : char.IsControl(character) ? '?' : character);
            }
            return builder.ToString().Trim();
        }

        private static string Sanitize(string? value, int maximumCharacters)
        {
            var bounded = LimitInput(value, maximumCharacters);
            var normalized = Normalize(bounded);
            try
            {
                normalized = SensitiveAssignmentRegex.Replace(normalized, "$1[REDACTED]");
                normalized = UriUserInfoRegex.Replace(normalized, "$1[REDACTED]@");
                return SensitiveQueryRegex.Replace(normalized, "$1[REDACTED]");
            }
            catch (RegexMatchTimeoutException)
            {
                return "[REDACTED]" + TruncatedMarker;
            }
        }

        private static string LimitInput(string? value, int maximumCharacters)
        {
            if (value == null || value.Length == 0) return string.Empty;
            if (value.Length <= maximumCharacters) return value;
            if (maximumCharacters <= TruncatedMarker.Length)
            {
                return TruncatedMarker.Substring(0, maximumCharacters);
            }
            return value.Substring(0, maximumCharacters - TruncatedMarker.Length) + TruncatedMarker;
        }
    }
}
