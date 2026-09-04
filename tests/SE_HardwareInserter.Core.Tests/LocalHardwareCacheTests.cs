using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using SE_HardwareInserter.Core.Library;
using Xunit;

namespace SE_HardwareInserter.Core.Tests
{
    public class LocalHardwareCacheTests : IDisposable
    {
        private readonly string _root;
        private readonly string _assemblyDirectory;
        private readonly string _libraryDirectory;

        public LocalHardwareCacheTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "SE_HardwareInserterCacheTests_" + Guid.NewGuid().ToString("N"));
            _assemblyDirectory = Path.Combine(_root, "Assembly");
            _libraryDirectory = Path.Combine(_root, "Library");
            Directory.CreateDirectory(_assemblyDirectory);
            Directory.CreateDirectory(_libraryDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        [Fact]
        public void EnsureLocalCopy_MismoContenido_ReutilizaEntradaVerificada()
        {
            var item = CreateItem("DIN", "M6.par", "version-1");
            var cache = new LocalHardwareCache();

            var first = cache.EnsureLocalCopy(_assemblyDirectory, item);
            var second = cache.EnsureLocalCopy(_assemblyDirectory, item);

            second.LocalPath.Should().Be(first.LocalPath);
            second.Sha256.Should().Be(first.Sha256);
            File.ReadAllText(second.LocalPath).Should().Be("version-1");
            Directory.EnumerateFiles(CacheRoot, "*.par", SearchOption.AllDirectories).Should().ContainSingle();
        }

        [Fact]
        public void EnsureLocalCopy_NuevaVersion_CreaOtraEntradaSinSobrescribir()
        {
            var item = CreateItem("DIN", "M6.par", "version-1");
            var cache = new LocalHardwareCache();
            var first = cache.EnsureLocalCopy(_assemblyDirectory, item);

            File.WriteAllText(item.FullPath, "version-2");
            var second = cache.EnsureLocalCopy(_assemblyDirectory, item);

            second.LocalPath.Should().NotBe(first.LocalPath);
            second.Sha256.Should().NotBe(first.Sha256);
            File.ReadAllText(first.LocalPath).Should().Be("version-1");
            File.ReadAllText(second.LocalPath).Should().Be("version-2");
        }

        [Fact]
        public void EnsureLocalCopy_MismoNombreDistintoContenido_NoColisiona()
        {
            var firstItem = CreateItem("DIN", "pieza.par", "contenido-a");
            var secondItem = CreateItem("ISO", "pieza.par", "contenido-b");
            var cache = new LocalHardwareCache();

            var first = cache.EnsureLocalCopy(_assemblyDirectory, firstItem);
            var second = cache.EnsureLocalCopy(_assemblyDirectory, secondItem);

            first.LocalPath.Should().NotBe(second.LocalPath);
            File.ReadAllText(first.LocalPath).Should().Be("contenido-a");
            File.ReadAllText(second.LocalPath).Should().Be("contenido-b");
        }

        [Fact]
        public void EnsureLocalCopy_MismoHashDistintoNombre_ComparteSlotConManifiestosSeparados()
        {
            var firstItem = CreateItem("DIN", "primero.par", "mismo-contenido");
            var secondItem = CreateItem("ISO", "segundo.par", "mismo-contenido");
            var cache = new LocalHardwareCache();

            var first = cache.EnsureLocalCopy(_assemblyDirectory, firstItem);
            var second = cache.EnsureLocalCopy(_assemblyDirectory, secondItem);

            Path.GetDirectoryName(second.LocalPath).Should().Be(Path.GetDirectoryName(first.LocalPath));
            File.Exists(first.LocalPath + ".manifest.json").Should().BeTrue();
            File.Exists(second.LocalPath + ".manifest.json").Should().BeTrue();
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(first.LocalPath)!, "hash.txt")).Trim()
                .Should().Be(first.Sha256);
        }

        [Fact]
        public void EnsureLocalCopy_PrefijoOcupadoPorOtroHash_ExtiendePrefijo()
        {
            var secondItem = CreateItem("DIN", "segundo.par", "contenido-b");
            var cache = new LocalHardwareCache();
            var secondHash = ComputeSha256(secondItem.FullPath);

            var occupiedDirectory = Path.Combine(CacheRoot, secondHash.Substring(0, 16));
            Directory.CreateDirectory(occupiedDirectory);
            var conflictingSuffix = new string(secondHash[16] == 'f' ? 'e' : 'f', 48);
            File.WriteAllText(
                Path.Combine(occupiedDirectory, "hash.txt"),
                secondHash.Substring(0, 16) + conflictingSuffix);

            var second = cache.EnsureLocalCopy(_assemblyDirectory, secondItem);

            new DirectoryInfo(Path.GetDirectoryName(second.LocalPath)!).Name.Should()
                .Be(secondHash.Substring(0, 24));
        }

        [Fact]
        public void EnsureLocalCopy_ProyectoMovido_ResuelveRutaRelativaEnNuevaUbicacion()
        {
            var item = CreateItem("DIN", "M6.par", "contenido");
            var first = new LocalHardwareCache().EnsureLocalCopy(_assemblyDirectory, item);
            var movedAssembly = Path.Combine(_root, "MovedAssembly");
            Directory.Move(_assemblyDirectory, movedAssembly);

            var moved = new LocalHardwareCache().EnsureLocalCopy(movedAssembly, item);

            moved.LocalRelativePath.Should().Be(first.LocalRelativePath);
            moved.LocalPath.Should().Be(Path.GetFullPath(Path.Combine(movedAssembly, moved.LocalRelativePath)));
            File.ReadAllText(moved.LocalPath).Should().Be("contenido");
        }

        [Fact]
        public async Task EnsureLocalCopy_DosInstanciasConcurrentes_CompartenEntradaCompleta()
        {
            var item = CreateItem("DIN", "M6.par", "contenido-concurrente");
            var firstCache = new LocalHardwareCache();
            var secondCache = new LocalHardwareCache();

            var firstTask = Task.Run(() => firstCache.EnsureLocalCopy(_assemblyDirectory, item));
            var secondTask = Task.Run(() => secondCache.EnsureLocalCopy(_assemblyDirectory, item));
            var results = await Task.WhenAll(firstTask, secondTask);
            var first = results[0];
            var second = results[1];

            first.LocalPath.Should().Be(second.LocalPath);
            File.Exists(first.LocalPath).Should().BeTrue();
            File.Exists(first.LocalPath + ".manifest.json").Should().BeTrue();
            Directory.EnumerateFiles(CacheRoot, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
        }

        [Fact]
        public void EnsureLocalCopy_PayloadSinManifiesto_ReconstruyeMetadatosSinRecopiar()
        {
            var item = CreateItem("DIN", "M6.par", "contenido");
            var cache = new LocalHardwareCache();
            var first = cache.EnsureLocalCopy(_assemblyDirectory, item);
            var manifestPath = first.LocalPath + ".manifest.json";
            var payloadTimestamp = File.GetLastWriteTimeUtc(first.LocalPath);
            File.Delete(manifestPath);

            var recovered = cache.EnsureLocalCopy(_assemblyDirectory, item);

            recovered.LocalPath.Should().Be(first.LocalPath);
            File.Exists(manifestPath).Should().BeTrue();
            File.GetLastWriteTimeUtc(recovered.LocalPath).Should().Be(payloadTimestamp);
            Directory.EnumerateFiles(CacheRoot, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
        }

        [Fact]
        public void EnsureLocalCopy_ResiduosTemporales_LimpiaSoloPatronesPropiosNoAbiertos()
        {
            var item = CreateItem("DIN", "M6.par", "contenido");
            var cache = new LocalHardwareCache();
            var first = cache.EnsureLocalCopy(_assemblyDirectory, item);
            var slotDirectory = Path.GetDirectoryName(first.LocalPath)!;
            var hashPath = Path.Combine(slotDirectory, "hash.txt");
            var manifestPath = first.LocalPath + ".manifest.json";
            var originalHashMetadata = File.ReadAllText(hashPath);
            var originalManifest = File.ReadAllText(manifestPath);
            var hashTemp = Path.Combine(slotDirectory, ".hash-stale.tmp");
            var copyTemp = Path.Combine(slotDirectory, ".copy-stale.tmp");
            var manifestTemp = Path.Combine(slotDirectory, ".manifest-stale.tmp");
            var openTemp = Path.Combine(slotDirectory, ".copy-open.tmp");
            var foreignTemp = Path.Combine(slotDirectory, ".other.tmp");
            File.WriteAllText(hashTemp, "residuo");
            File.WriteAllText(copyTemp, "residuo");
            File.WriteAllText(manifestTemp, "residuo");
            File.WriteAllText(openTemp, "abierto");
            File.WriteAllText(foreignTemp, "ajeno");

            using (new FileStream(openTemp, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                cache.EnsureLocalCopy(_assemblyDirectory, item);

                File.Exists(openTemp).Should().BeTrue("un temporal abierto no se debe borrar");
            }

            File.Exists(hashTemp).Should().BeFalse();
            File.Exists(copyTemp).Should().BeFalse();
            File.Exists(manifestTemp).Should().BeFalse();
            File.Exists(foreignTemp).Should().BeTrue("no pertenece a los patrones de la caché");
            File.Exists(first.LocalPath).Should().BeTrue();
            File.ReadAllText(hashPath).Should().Be(originalHashMetadata);
            File.ReadAllText(manifestPath).Should().Be(originalManifest);

            cache.EnsureLocalCopy(_assemblyDirectory, item);
            File.Exists(openTemp).Should().BeFalse("se limpia cuando deja de estar abierto");
        }

        [Fact]
        public void EnsureLocalCopy_FuenteInexistente_NoCreaCarpetaHardware()
        {
            var missingPath = Path.Combine(_libraryDirectory, "missing.par");
            var item = new LibraryItem(missingPath, "missing.par", "(Raíz)", "missing.par");
            var cache = new LocalHardwareCache();

            Action action = () => cache.EnsureLocalCopy(_assemblyDirectory, item);

            action.Should().Throw<FileNotFoundException>();
            Directory.Exists(Path.Combine(_assemblyDirectory, "Hardware")).Should().BeFalse();
        }

        [Fact]
        public void EnsureLocalCopy_DestinoNoCreable_NoDejaTemporales()
        {
            var item = CreateItem("DIN", "M6.par", "contenido");
            File.WriteAllText(Path.Combine(_assemblyDirectory, "Hardware"), "bloqueo");
            var cache = new LocalHardwareCache();

            Action action = () => cache.EnsureLocalCopy(_assemblyDirectory, item);

            action.Should().Throw<IOException>();
            Directory.EnumerateFiles(_root, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
        }

        [Fact]
        public void EnsureLocalCopy_ManifiestoIncluyeTrazabilidadYUtc()
        {
            var item = CreateItem("DIN", "M6.par", "contenido");
            var result = new LocalHardwareCache().EnsureLocalCopy(_assemblyDirectory, item);
            var manifestPath = result.LocalPath + ".manifest.json";

            var json = File.ReadAllText(manifestPath);

            json.Should().Contain("\"sha256\"");
            json.Should().Contain("\"sourceFullPath\"");
            json.Should().Contain("\"sourceRelativePath\"");
            json.Should().Contain("\"localRelativePath\"");
            json.Should().NotContain("\"localPath\"");
            json.Should().Contain("\"createdUtc\"");
            result.Sha256.Should().Be(ComputeSha256(item.FullPath));
            result.SourceFullPath.Should().Be(Path.GetFullPath(item.FullPath));
            result.SourceRelativePath.Should().Be(item.RelativePath);
            result.LocalRelativePath.Should().Be(PathHelper.GetRelativePath(_assemblyDirectory, result.LocalPath));
            result.CreatedUtc.Kind.Should().Be(DateTimeKind.Utc);
            Directory.EnumerateFiles(CacheRoot, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
        }

        [Fact]
        public void EnsureLocalCopy_ExtensionDistintaDePar_RechazaAntesDeCrearCache()
        {
            var path = Path.Combine(_libraryDirectory, "pieza.txt");
            File.WriteAllText(path, "contenido");
            var item = new LibraryItem(path, "pieza.txt", "(Raíz)", "pieza.txt");

            Action action = () => new LocalHardwareCache().EnsureLocalCopy(_assemblyDirectory, item);

            action.Should().Throw<ArgumentException>();
            Directory.Exists(CacheRoot).Should().BeFalse();
        }

        [Fact]
        public void EnsureLocalCopy_DirectorioEnsamblajeInexistente_RechazaAntesDeCrearCache()
        {
            var item = CreateItem("DIN", "M6.par", "contenido");
            var missingAssembly = Path.Combine(_root, "MissingAssembly");

            Action action = () => new LocalHardwareCache().EnsureLocalCopy(missingAssembly, item);

            action.Should().Throw<DirectoryNotFoundException>();
            Directory.Exists(missingAssembly).Should().BeFalse();
        }

        [Fact]
        public void EnsureLocalCopy_ArchivoLegacyConMismoNombre_PermaneceIntacto()
        {
            var item = CreateItem("DIN", "M6.par", "nuevo");
            var hardwareDirectory = Path.Combine(_assemblyDirectory, "Hardware");
            Directory.CreateDirectory(hardwareDirectory);
            var legacyPath = Path.Combine(hardwareDirectory, "M6.par");
            File.WriteAllText(legacyPath, "legacy");

            var result = new LocalHardwareCache().EnsureLocalCopy(_assemblyDirectory, item);

            result.LocalPath.StartsWith(CacheRoot, StringComparison.OrdinalIgnoreCase).Should().BeTrue();
            File.ReadAllText(legacyPath).Should().Be("legacy");
        }

        private string CacheRoot => Path.Combine(
            _assemblyDirectory,
            "Hardware",
            ".se-hardware-inserter");

        private LibraryItem CreateItem(string category, string filename, string content)
        {
            var directory = Path.Combine(_libraryDirectory, category);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, filename);
            File.WriteAllText(path, content);
            return new LibraryItem(path, Path.Combine(category, filename), category, filename);
        }

        private static string ComputeSha256(string filePath)
        {
            using (var stream = File.OpenRead(filePath))
            using (var sha256 = SHA256.Create())
            {
                var builder = new StringBuilder();
                foreach (var value in sha256.ComputeHash(stream))
                {
                    builder.Append(value.ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}
