#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security;
using System.Xml;

namespace SE_HardwareInserter.Core.Preferences
{
    /// <summary>
    /// Persiste <see cref="Settings"/> mediante JSON y migra la ubicación usada antes del
    /// renombrado del add-in.
    /// </summary>
    public sealed class SettingsStore
    {
        private const string SettingsFileName = "settings.json";
        private readonly string _filePath;
        private readonly string? _legacyFilePath;

        /// <summary>Inicializa el almacén con las ubicaciones canónica y legacy de <c>%AppData%</c>.</summary>
        public SettingsStore()
            : this(GetDefaultFilePath(), GetLegacyFilePath())
        {
        }

        /// <summary>
        /// Inicializa un almacén sobre una ruta explícita. <paramref name="legacyFilePath"/> puede
        /// indicar una ubicación que se migrará únicamente si la ruta canónica todavía no existe.
        /// </summary>
        public SettingsStore(string filePath, string? legacyFilePath = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("La ruta de configuración no puede estar vacía.", nameof(filePath));
            }

            _filePath = filePath;
            _legacyFilePath = legacyFilePath;
        }

        /// <summary>Ruta del archivo administrado por esta instancia.</summary>
        public string FilePath => _filePath;

        /// <summary>Devuelve la ruta canónica en <c>%AppData%\SE_HardwareInserter</c>.</summary>
        public static string GetDefaultFilePath()
        {
            return BuildAppDataPath("SE_HardwareInserter");
        }

        /// <summary>Devuelve la ruta usada por versiones anteriores del add-in.</summary>
        public static string GetLegacyFilePath()
        {
            return BuildAppDataPath("HardwareInserter");
        }

        /// <summary>
        /// Carga las preferencias. Los errores de formato o acceso no impiden arrancar el add-in;
        /// cuando es posible, un JSON ilegible se conserva junto al original con sufijo
        /// <c>.corrupt-&lt;UTC timestamp&gt;</c>.
        /// </summary>
        public Settings Load()
        {
            if (File.Exists(_filePath))
            {
                return TryRead(_filePath, preserveUnreadableFile: true, out var settings)
                    ? settings
                    : new Settings();
            }

            var legacyFilePath = _legacyFilePath;
            if (legacyFilePath == null
                || string.IsNullOrWhiteSpace(legacyFilePath)
                || PathsEqual(_filePath, legacyFilePath)
                || !File.Exists(legacyFilePath))
            {
                return new Settings();
            }

            if (!TryRead(legacyFilePath, preserveUnreadableFile: true, out var legacySettings))
            {
                return new Settings();
            }

            try
            {
                Save(legacySettings);
                if (!TryRead(_filePath, preserveUnreadableFile: false, out var verifiedSettings)
                    || !string.Equals(
                        verifiedSettings.RootPath,
                        legacySettings.RootPath,
                        StringComparison.Ordinal))
                {
                    return legacySettings;
                }

                DeleteMigratedLegacyFile(legacyFilePath);
                return verifiedSettings;
            }
            catch (Exception exception) when (IsRecoverableFileException(exception))
            {
                // La configuración legacy sigue siendo utilizable y no se elimina si la migración falla.
                return legacySettings;
            }
        }

        /// <summary>
        /// Guarda las preferencias mediante un archivo temporal en el mismo directorio y una
        /// sustitución atómica del destino existente.
        /// </summary>
        public void Save(Settings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var serializer = CreateSerializer();
                using (var stream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                {
                    serializer.WriteObject(stream, settings);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(_filePath))
                {
                    File.Replace(tempPath, _filePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, _filePath);
                }
            }
            finally
            {
                TryDeleteFile(tempPath);
            }
        }

        private static string BuildAppDataPath(string productDirectoryName)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, productDirectoryName, SettingsFileName);
        }

        private static DataContractJsonSerializer CreateSerializer()
        {
            return new DataContractJsonSerializer(typeof(Settings));
        }

        private static bool TryRead(string filePath, bool preserveUnreadableFile, out Settings settings)
        {
            try
            {
                var serializer = CreateSerializer();
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    settings = serializer.ReadObject(stream) as Settings ?? new Settings();
                }

                settings.RootPath = settings.RootPath ?? string.Empty;
                return true;
            }
            catch (Exception exception) when (IsRecoverableLoadException(exception))
            {
                if (preserveUnreadableFile)
                {
                    PreserveUnreadableFile(filePath);
                }

                settings = new Settings();
                return false;
            }
        }

        private static void PreserveUnreadableFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return;
                }

                var timestamp = DateTime.UtcNow.ToString(
                    "yyyyMMdd'T'HHmmssfffffff'Z'",
                    CultureInfo.InvariantCulture);
                File.Move(filePath, filePath + ".corrupt-" + timestamp);
            }
            catch (Exception exception) when (IsRecoverableFileException(exception))
            {
                // Si el sistema deniega el movimiento, Load sigue devolviendo valores seguros.
            }
        }

        private static void DeleteMigratedLegacyFile(string legacyFilePath)
        {
            try
            {
                File.Delete(legacyFilePath);

                var legacyDirectory = Path.GetDirectoryName(legacyFilePath);
                if (!string.IsNullOrEmpty(legacyDirectory)
                    && Directory.Exists(legacyDirectory)
                    && !Directory.EnumerateFileSystemEntries(legacyDirectory).Any())
                {
                    Directory.Delete(legacyDirectory);
                }
            }
            catch (Exception exception) when (IsRecoverableFileException(exception))
            {
                // La migración ya fue verificada; no se compromete el arranque por residuos legacy.
            }
        }

        private static void TryDeleteFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception exception) when (IsRecoverableFileException(exception))
            {
                // Un temporal bloqueado se podrá retirar posteriormente; el destino no se altera.
            }
        }

        private static bool PathsEqual(string first, string second)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(first),
                    Path.GetFullPath(second),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (exception is ArgumentException
                || exception is NotSupportedException
                || exception is PathTooLongException
                || exception is SecurityException)
            {
                return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static bool IsRecoverableLoadException(Exception exception)
        {
            return IsRecoverableFileException(exception)
                || exception is SerializationException
                || exception is XmlException;
        }

        private static bool IsRecoverableFileException(Exception exception)
        {
            return exception is IOException
                || exception is UnauthorizedAccessException
                || exception is SecurityException
                || exception is NotSupportedException
                || exception is ArgumentException;
        }
    }
}
