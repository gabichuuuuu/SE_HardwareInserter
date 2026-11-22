using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using FluentAssertions;
using SE_HardwareInserter.Core.Library;
using Xunit;

namespace SE_HardwareInserter.Core.Tests
{
    public class FileSystemFastenerLibraryTests : IDisposable
    {
        private readonly string _root;

        public FileSystemFastenerLibraryTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "SE_HardwareInserterTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        [Fact]
        public void Constructor_CarpetaInexistente_LanzaDirectoryNotFoundException()
        {
            var rutaInexistente = Path.Combine(Path.GetTempPath(), "no_existe_" + Guid.NewGuid().ToString("N"));
            Action accion = () => new FileSystemFastenerLibrary(rutaInexistente);
            accion.Should().Throw<DirectoryNotFoundException>();
        }

        [Fact]
        public void Items_CarpetaVacia_DevuelveListaVacia()
        {
            using var lib = new FileSystemFastenerLibrary(_root);
            lib.Items.Should().BeEmpty();
        }

        [Fact]
        public void Constructor_CarpetaConPar_CargaItemConRutaYCategoria()
        {
            var subDir = Path.Combine(_root, "DIN912");
            Directory.CreateDirectory(subDir);
            var par = Path.Combine(subDir, "DIN912_M6x16.par");
            File.WriteAllText(par, "placeholder");

            using var lib = new FileSystemFastenerLibrary(_root);

            lib.Items.Should().HaveCount(1);
            var item = lib.Items.Single();
            item.Filename.Should().Be("DIN912_M6x16.par");
            item.Category.Should().Be("DIN912");
            item.FullPath.Should().Be(par);
            item.RelativePath.Should().Be(Path.Combine("DIN912", "DIN912_M6x16.par"));
        }

        [Fact]
        public void Refresh_CarpetaConPar_ActualizaElSnapshot()
        {
            using var lib = new FileSystemFastenerLibrary(_root);
            File.WriteAllText(Path.Combine(_root, "manual.par"), "x");

            lib.Refresh();

            lib.Items.Should().ContainSingle(i => i.Filename == "manual.par");
        }

        [Fact]
        public void Refresh_SubcarpetasAnidadas_AgrupaPorCarpetaInmediata()
        {
            // Raíz / Tornillería / DIN912 / M6 / DIN912_M6x16.par
            var nestedDir = Path.Combine(_root, "Tornillería", "DIN912", "M6");
            Directory.CreateDirectory(nestedDir);
            File.WriteAllText(Path.Combine(nestedDir, "DIN912_M6x16.par"), "x");
            File.WriteAllText(Path.Combine(_root, "root.par"), "x");

            using var lib = new FileSystemFastenerLibrary(_root);
            lib.Refresh();

            lib.Items.Should().HaveCount(2);
            var rootItem = lib.Items.Single(i => i.Filename == "root.par");
            rootItem.Category.Should().Be("(Raíz)");
            var nestedItem = lib.Items.Single(i => i.Filename == "DIN912_M6x16.par");
            nestedItem.Category.Should().Be("Tornillería");
        }

        [Fact]
        public void Refresh_SoloPar_OtrosArchivosSonIgnorados()
        {
            File.WriteAllText(Path.Combine(_root, "doc.txt"), "x");
            File.WriteAllText(Path.Combine(_root, "drawing.dxf"), "x");
            File.WriteAllText(Path.Combine(_root, "screw.PAR"), "x");

            using var lib = new FileSystemFastenerLibrary(_root);
            lib.Refresh();

            lib.Items.Should().HaveCount(1);
            lib.Items.Single().Filename.Should().Be("screw.PAR");
        }

        [Fact]
        public void SetRootPath_NuevaCarpeta_ReescaneaItems()
        {
            File.WriteAllText(Path.Combine(_root, "a.par"), "x");
            using var lib = new FileSystemFastenerLibrary(_root);
            lib.Refresh();
            lib.Items.Should().HaveCount(1);

            var otraRaiz = Path.Combine(Path.GetTempPath(), "SE_HardwareInserterTests_otra_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(otraRaiz);
            try
            {
                File.WriteAllText(Path.Combine(otraRaiz, "b.par"), "x");
                File.WriteAllText(Path.Combine(otraRaiz, "c.par"), "x");

                lib.SetRootPath(otraRaiz);

                lib.Items.Should().HaveCount(2);
                lib.RootPath.Should().Be(Path.GetFullPath(otraRaiz));
            }
            finally
            {
                if (Directory.Exists(otraRaiz)) Directory.Delete(otraRaiz, recursive: true);
            }
        }

        [Fact]
        public void Changed_NuevoPar_ApareceTrasWatch()
        {
            using var lib = new FileSystemFastenerLibrary(_root);
            using var changed = new ManualResetEventSlim();
            lib.Changed += (s, e) => changed.Set();

            // Crear un .par nuevo en la raíz debe disparar el FileSystemWatcher.
            File.WriteAllText(Path.Combine(_root, "nuevo.par"), "x");

            changed.Wait(TimeSpan.FromSeconds(5)).Should()
                .BeTrue("el FileSystemWatcher debería haber disparado Changed al crear un .par");
            lib.Items.Should().Contain(i => i.Filename == "nuevo.par");
        }

        [Fact]
        public void SetRootPath_RutaInexistente_ConservaRaizYWatcherAnteriores()
        {
            using var lib = new FileSystemFastenerLibrary(_root);
            var rutaInexistente = Path.Combine(Path.GetTempPath(), "no_existe_" + Guid.NewGuid().ToString("N"));

            Action changeRoot = () => lib.SetRootPath(rutaInexistente);

            changeRoot.Should().Throw<DirectoryNotFoundException>();
            lib.RootPath.Should().Be(Path.GetFullPath(_root));

            using var changed = new ManualResetEventSlim();
            lib.Changed += (s, e) => changed.Set();
            File.WriteAllText(Path.Combine(_root, "watcher-sigue-activo.par"), "x");

            changed.Wait(TimeSpan.FromSeconds(5)).Should()
                .BeTrue("un cambio de raíz fallido no debe desactivar el watcher anterior");
            lib.Items.Should().ContainSingle(i => i.Filename == "watcher-sigue-activo.par");
        }

        [Fact]
        public void RefreshGeneration_TokenDeRaizAnterior_NoEjecutaOtroEscaneo()
        {
            var otraRaiz = Path.Combine(Path.GetTempPath(), "SE_HardwareInserterTests_nueva_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(otraRaiz);
            try
            {
                var scanCount = 0;
                List<LibraryItem> Scanner(string root)
                {
                    Interlocked.Increment(ref scanCount);
                    return new List<LibraryItem>();
                }

                using var lib = new FileSystemFastenerLibrary(_root, Scanner);
                var staleGeneration = lib.CaptureGeneration();
                lib.SetRootPath(otraRaiz);
                var countAfterRootChange = scanCount;

                lib.RefreshGeneration(staleGeneration);

                lib.RootPath.Should().Be(Path.GetFullPath(otraRaiz));
                scanCount.Should().Be(countAfterRootChange,
                    "un token de una raíz anterior debe descartarse antes de invocar al scanner");
            }
            finally
            {
                if (Directory.Exists(otraRaiz)) Directory.Delete(otraRaiz, recursive: true);
            }
        }

        [Fact]
        public void Constructor_FalloDeEscaneoInicial_PropagaError()
        {
            Func<string, List<LibraryItem>> failingScanner = root =>
                throw new IOException("fallo inicial controlado");

            Action action = () => new FileSystemFastenerLibrary(_root, failingScanner);

            action.Should().Throw<IOException>()
                .WithMessage("fallo inicial controlado");
        }

        [Fact]
        public void Refresh_FalloDeEscaneo_ConservaSnapshotYExponeError()
        {
            var existingPath = Path.Combine(_root, "existente.par");
            File.WriteAllText(existingPath, "x");
            var failScan = false;
            List<LibraryItem> Scanner(string root)
            {
                if (failScan) throw new IOException("fallo de refresh controlado");
                return new List<LibraryItem>
                {
                    new LibraryItem(existingPath, "existente.par", "(Raíz)", "existente.par"),
                };
            }

            using var lib = new FileSystemFastenerLibrary(_root, Scanner);
            LibraryErrorEventArgs? reportedError = null;
            lib.Error += (s, e) => reportedError = e;
            failScan = true;

            lib.Refresh();

            lib.Items.Should().ContainSingle(i => i.Filename == "existente.par");
            lib.LastError.Should().BeAssignableTo<IOException>();
            reportedError.Should().NotBeNull();
            reportedError!.RootPath.Should().Be(Path.GetFullPath(_root));
            reportedError.Exception.Should().BeSameAs(lib.LastError);
        }

        [Fact]
        public void Dispose_DosVeces_EsSeguroYRefreshFallaDeFormaExplicita()
        {
            var lib = new FileSystemFastenerLibrary(_root);

            lib.Dispose();
            lib.Dispose();
            Action refresh = () => lib.Refresh();

            refresh.Should().Throw<ObjectDisposedException>();
        }

        [Fact]
        public void Items_OrdenadoPorRutaRelativa()
        {
            Directory.CreateDirectory(Path.Combine(_root, "z"));
            Directory.CreateDirectory(Path.Combine(_root, "a"));
            File.WriteAllText(Path.Combine(_root, "z", "tornillo.par"), "x");
            File.WriteAllText(Path.Combine(_root, "a", "tornillo.par"), "x");
            File.WriteAllText(Path.Combine(_root, "root.par"), "x");

            using var lib = new FileSystemFastenerLibrary(_root);
            lib.Refresh();

            var relativas = lib.Items.Select(i => i.RelativePath).ToList();
            relativas.Should().BeInAscendingOrder();
        }

        [Fact]
        public void Changed_EliminarPar_DesapareceTrasWatch()
        {
            var parPath = Path.Combine(_root, "eliminar.par");
            File.WriteAllText(parPath, "x");
            using var lib = new FileSystemFastenerLibrary(_root);
            lib.Refresh();
            lib.Items.Should().HaveCount(1);

            using var changed = new ManualResetEventSlim();
            lib.Changed += (s, e) => changed.Set();

            File.Delete(parPath);

            changed.Wait(TimeSpan.FromSeconds(5)).Should()
                .BeTrue("el FileSystemWatcher debería haber disparado Changed al borrar un .par");
            lib.Items.Should().BeEmpty();
        }
    }
}
