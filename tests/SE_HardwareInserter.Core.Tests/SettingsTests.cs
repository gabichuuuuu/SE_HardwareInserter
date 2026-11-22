using System;
using System.IO;
using FluentAssertions;
using SE_HardwareInserter.Core.Preferences;
using Xunit;

namespace SE_HardwareInserter.Core.Tests
{
    public class SettingsTests : IDisposable
    {
        private readonly string _testDirectory;
        private readonly string _settingsPath;
        private readonly string _legacySettingsPath;

        public SettingsTests()
        {
            _testDirectory = Path.Combine(
                Path.GetTempPath(),
                "SE_HardwareInserter_settings_" + Guid.NewGuid().ToString("N"));
            _settingsPath = Path.Combine(_testDirectory, "new", "settings.json");
            _legacySettingsPath = Path.Combine(_testDirectory, "legacy", "settings.json");
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }

        [Fact]
        public void Load_ArchivoInexistente_DevuelveInstanciaVacia()
        {
            var store = new SettingsStore(_settingsPath);

            var settings = store.Load();

            settings.RootPath.Should().BeEmpty();
        }

        [Fact]
        public void Save_CreaArchivoYDirectorio_ConContratoJsonCompatible()
        {
            var store = new SettingsStore(_settingsPath);

            store.Save(new Settings { RootPath = @"C:\Tornilleria" });

            File.Exists(_settingsPath).Should().BeTrue();
            var json = File.ReadAllText(_settingsPath);
            json.Should().Contain("\"rootPath\"");
            json.Should().Contain("Tornilleria");
        }

        [Fact]
        public void SaveYLoad_RoundTrip_PreservaRootPath()
        {
            var store = new SettingsStore(_settingsPath);
            store.Save(new Settings { RootPath = @"C:\Tornilleria" });

            var loaded = store.Load();

            loaded.RootPath.Should().Be(@"C:\Tornilleria");
        }

        [Fact]
        public void Load_JsonCorrupto_LoConservaYDevuelveInstanciaVacia()
        {
            var settingsDirectory = Path.GetDirectoryName(_settingsPath);
            Directory.CreateDirectory(settingsDirectory);
            File.WriteAllText(_settingsPath, "{ no es json válido");
            var store = new SettingsStore(_settingsPath);

            var loaded = store.Load();

            loaded.RootPath.Should().BeEmpty();
            File.Exists(_settingsPath).Should().BeFalse();
            Directory.GetFiles(settingsDirectory, "settings.json.corrupt-*")
                .Should()
                .ContainSingle();
        }

        [Fact]
        public void Save_SustituyeDestinoSinDejarTemporales()
        {
            var store = new SettingsStore(_settingsPath);
            store.Save(new Settings { RootPath = @"C:\Anterior" });

            store.Save(new Settings { RootPath = @"C:\Nueva" });

            store.Load().RootPath.Should().Be(@"C:\Nueva");
            Directory.GetFiles(
                    Path.GetDirectoryName(_settingsPath),
                    "settings.json.*.tmp")
                .Should()
                .BeEmpty();
        }

        [Fact]
        public void Load_ConfiguracionLegacyValida_MigraSinPerdidaYRetiraDirectorioVacio()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_legacySettingsPath));
            File.WriteAllText(_legacySettingsPath, "{\"rootPath\":\"C:\\\\Legacy\"}");
            var store = new SettingsStore(_settingsPath, _legacySettingsPath);

            var loaded = store.Load();

            loaded.RootPath.Should().Be(@"C:\Legacy");
            new SettingsStore(_settingsPath).Load().RootPath.Should().Be(@"C:\Legacy");
            File.Exists(_legacySettingsPath).Should().BeFalse();
            Directory.Exists(Path.GetDirectoryName(_legacySettingsPath)).Should().BeFalse();
        }

        [Fact]
        public void Load_CanonicoExistente_NoLoSobrescribeConLegacy()
        {
            var canonicalStore = new SettingsStore(_settingsPath);
            canonicalStore.Save(new Settings { RootPath = @"C:\Canonico" });
            Directory.CreateDirectory(Path.GetDirectoryName(_legacySettingsPath));
            File.WriteAllText(_legacySettingsPath, "{\"rootPath\":\"C:\\\\Legacy\"}");
            var migratingStore = new SettingsStore(_settingsPath, _legacySettingsPath);

            var loaded = migratingStore.Load();

            loaded.RootPath.Should().Be(@"C:\Canonico");
            File.Exists(_legacySettingsPath).Should().BeTrue();
        }

        [Fact]
        public void GetDefaultFilePath_DevuelveRutaBajoAppData()
        {
            var path = SettingsStore.GetDefaultFilePath();

            path.Should().EndWith("settings.json");
            Path.GetDirectoryName(path).Should().Contain("SE_HardwareInserter");
        }
    }
}
