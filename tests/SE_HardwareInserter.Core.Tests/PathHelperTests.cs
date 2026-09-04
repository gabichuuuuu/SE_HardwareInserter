using System.IO;
using FluentAssertions;
using SE_HardwareInserter.Core.Library;
using Xunit;

namespace SE_HardwareInserter.Core.Tests
{
    public class PathHelperTests
    {
        [Fact]
        public void GetRelativePath_RutasHermanas_RespetaLimitesDeSegmento()
        {
            var commonRoot = Path.Combine(Path.GetTempPath(), "segmentos");
            var result = PathHelper.GetRelativePath(
                Path.Combine(commonRoot, "bar"),
                Path.Combine(commonRoot, "barista", "pieza.par"));

            result.Should().Be(Path.Combine("..", "barista", "pieza.par"));
        }

        [Fact]
        public void GetRelativePath_WindowsDiferenteCasing_ConsideraSegmentosIguales()
        {
            if (!IsWindows) return;

            var result = PathHelper.GetRelativePath(@"C:\LIBRERIA\DIN", @"c:\libreria\din\M6.par");

            result.Should().Be("M6.par");
        }

        [Fact]
        public void GetRelativePath_MismaRutaConSeparadorFinal_DevuelvePunto()
        {
            var path = Path.Combine(Path.GetTempPath(), "Libreria", "DIN");
            var result = PathHelper.GetRelativePath(path + Path.DirectorySeparatorChar, path);

            result.Should().Be(".");
        }

        [Fact]
        public void GetRelativePath_UncMismoShare_CalculaPorSegmentos()
        {
            if (!IsWindows) return;

            var result = PathHelper.GetRelativePath(
                @"\\servidor\hardware\DIN\M6",
                @"\\SERVIDOR\HARDWARE\DIN\M8\pieza.par");

            result.Should().Be(Path.Combine("..", "M8", "pieza.par"));
        }

        [Fact]
        public void GetRelativePath_UncShareDistinto_DevuelveDestinoAbsoluto()
        {
            if (!IsWindows) return;

            const string target = @"\\servidor\otro-share\pieza.par";

            var result = PathHelper.GetRelativePath(@"\\servidor\hardware\DIN", target);

            result.Should().Be(Path.GetFullPath(target));
        }

        [Fact]
        public void GetRelativePath_UnidadDistinta_DevuelveDestinoAbsoluto()
        {
            if (!IsWindows) return;

            const string target = @"D:\hardware\pieza.par";

            var result = PathHelper.GetRelativePath(@"C:\hardware", target);

            result.Should().Be(Path.GetFullPath(target));
        }

        private static bool IsWindows => Path.DirectorySeparatorChar == '\\';
    }
}
