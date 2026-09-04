using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using FluentAssertions;
using SE_HardwareInserter.Core.Diagnostics;
using Xunit;

namespace SE_HardwareInserter.Core.Tests
{
    public sealed class FileAppLoggerTests : IDisposable
    {
        private readonly string _testDirectory = Path.Combine(
            Path.GetTempPath(),
            "SE_HardwareInserter_logs_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_testDirectory)) Directory.Delete(_testDirectory, recursive: true);
            else if (File.Exists(_testDirectory)) File.Delete(_testDirectory);
        }

        [Fact]
        public void Info_CuandoSuperaLimite_RotaSinPerderEntradaActual()
        {
            var now = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
            var logger = new FileAppLogger(_testDirectory, maximumBytes: 80, utcNow: () => now);

            logger.Info("op-1", "copy", new string('a', 90));
            logger.Info("op-2", "commit", "segunda entrada");

            var files = Directory.GetFiles(_testDirectory, "*.log");
            files.Should().HaveCount(2);
            files.Should().OnlyContain(path => new FileInfo(path).Length <= 80);
            files.Should().Contain(path => path.EndsWith("SE_HardwareInserter-20260904-001.log"));
            File.ReadAllText(Path.Combine(_testDirectory, "SE_HardwareInserter-20260904.log"))
                .Should().Contain("op-2").And.NotContain("op-1");
        }

        [Fact]
        public void Info_EliminaLogsFueraDeRetencion()
        {
            var now = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);
            Directory.CreateDirectory(_testDirectory);
            var expired = Path.Combine(_testDirectory, "SE_HardwareInserter-20260820.log");
            File.WriteAllText(expired, "old");
            File.SetLastWriteTimeUtc(expired, now.AddDays(-15));
            var logger = new FileAppLogger(_testDirectory, retention: TimeSpan.FromDays(14), utcNow: () => now);

            logger.Info("op", "startup", "activo");

            File.Exists(expired).Should().BeFalse();
            File.Exists(Path.Combine(_testDirectory, "SE_HardwareInserter-20260904.log")).Should().BeTrue();
        }

        [Fact]
        public void Error_DestinoNoEsDirectorio_NoPropagaExcepcion()
        {
            File.WriteAllText(_testDirectory, "bloqueo");
            var logger = new FileAppLogger(_testDirectory);

            Action action = () => logger.Error("op", "test", "fallo", new IOException("simulado"));

            action.Should().NotThrow();
        }

        [Fact]
        public void Error_SaneaSecretosEnMensajeExcepcionYContexto()
        {
            var logger = new FileAppLogger(_testDirectory);

            logger.Error(
                "op",
                "security",
                "token=abc123 uri=https://user:pass@example.test/path?secret=qwerty",
                new InvalidOperationException("password: hunter2"),
                new Dictionary<string, string>
                {
                    ["authorization"] = "Bearer private",
                    ["endpoint"] = "https://name:key@example.test/?apikey=value",
                });

            var text = File.ReadAllText(Directory.GetFiles(_testDirectory, "*.log")[0]);
            text.Should().Contain("[REDACTED]");
            text.Should().NotContain("abc123");
            text.Should().NotContain("user:pass");
            text.Should().NotContain("qwerty");
            text.Should().NotContain("hunter2");
            text.Should().NotContain("Bearer private");
            text.Should().NotContain("name:key");
            text.Should().NotContain("value");
        }

        [Fact]
        public void Error_EntradaDeCincoMegabytes_SeLimitaAntesDeSanear()
        {
            var logger = new FileAppLogger(_testDirectory);
            var giantMessage = "token=secret-at-start "
                + new string('x', 5 * 1024 * 1024)
                + " password=secret-at-end";
            var giantContext = "authorization=context-secret " + new string('y', 5 * 1024 * 1024);
            var context = new Dictionary<string, string> { ["details"] = giantContext };
            var stopwatch = Stopwatch.StartNew();

            logger.Error("op", "performance", giantMessage, context: context);
            stopwatch.Stop();

            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
            var path = Directory.GetFiles(_testDirectory, "*.log")[0];
            new FileInfo(path).Length.Should().BeLessOrEqualTo(5L * 1024L * 1024L);
            var text = File.ReadAllText(path);
            text.Should().Contain("[TRUNCATED]");
            text.Should().Contain("[REDACTED]");
            text.Should().NotContain("secret-at-start");
            text.Should().NotContain("secret-at-end");
            text.Should().NotContain("context-secret");
        }
    }
}
