using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace SE_HardwareInserter.Core.Library
{
    /// <summary>Resultado inmutable de almacenar un archivo de hardware por contenido.</summary>
    [DataContract]
    public sealed class LocalHardwareCopy
    {
        private LocalHardwareCopy()
        {
            Sha256 = string.Empty;
            SourceFullPath = string.Empty;
            SourceRelativePath = string.Empty;
            LocalRelativePath = string.Empty;
            LocalPath = string.Empty;
        }

        internal LocalHardwareCopy(
            string sha256,
            string sourceFullPath,
            string sourceRelativePath,
            string localRelativePath,
            string localPath,
            DateTime createdUtc)
        {
            Sha256 = sha256;
            SourceFullPath = sourceFullPath;
            SourceRelativePath = sourceRelativePath;
            LocalRelativePath = localRelativePath;
            LocalPath = localPath;
            CreatedUtc = createdUtc;
        }

        /// <summary>SHA-256 completo, en hexadecimal minúscula, del archivo almacenado.</summary>
        [DataMember(Name = "sha256", Order = 1)]
        public string Sha256 { get; private set; }

        /// <summary>Ruta absoluta del archivo maestro que originó esta copia.</summary>
        [DataMember(Name = "sourceFullPath", Order = 2)]
        public string SourceFullPath { get; private set; }

        /// <summary>Ruta del archivo maestro relativa a la raíz de su librería.</summary>
        [DataMember(Name = "sourceRelativePath", Order = 3)]
        public string SourceRelativePath { get; private set; }

        /// <summary>Ruta local relativa al directorio del ensamblaje.</summary>
        [DataMember(Name = "localRelativePath", Order = 4)]
        public string LocalRelativePath { get; private set; }

        /// <summary>Ruta absoluta de la copia, resuelta para la ubicación actual del ensamblaje.</summary>
        [IgnoreDataMember]
        public string LocalPath { get; private set; }

        /// <summary>Instante UTC en el que se creó el manifiesto del payload.</summary>
        [DataMember(Name = "createdUtc", Order = 5)]
        public DateTime CreatedUtc { get; private set; }

        internal LocalHardwareCopy ResolveAt(string localPath, string localRelativePath)
        {
            return new LocalHardwareCopy(
                Sha256,
                SourceFullPath,
                SourceRelativePath,
                localRelativePath,
                localPath,
                CreatedUtc);
        }
    }

    /// <summary>
    /// Caché local inmutable de archivos <c>.par</c>, aislada de la carpeta heredada
    /// <c>Hardware</c> y direccionada por SHA-256 completo.
    /// </summary>
    public sealed class LocalHardwareCache
    {
        private const string HardwareFolderName = "Hardware";
        private const string CacheFolderName = ".se-hardware-inserter";
        private const string SlotHashFileName = "hash.txt";
        private const string SlotLockFileName = ".slot.lock";
        private const int InitialPrefixLength = 16;
        private const int PrefixGrowth = 8;
        private const int LockRetryMilliseconds = 25;
        private const int LockTimeoutMilliseconds = 3000;

        /// <summary>
        /// Devuelve una copia local verificada de <paramref name="source"/> dentro del directorio
        /// del ensamblaje. Nunca sobrescribe un payload existente ni toca la caché heredada.
        /// </summary>
        public LocalHardwareCopy EnsureLocalCopy(string assemblyDirectory, LibraryItem source)
        {
            if (string.IsNullOrWhiteSpace(assemblyDirectory))
            {
                throw new ArgumentException("El directorio del ensamblaje no puede estar vacío.", nameof(assemblyDirectory));
            }
            if (source == null) throw new ArgumentNullException(nameof(source));

            var normalizedAssemblyDirectory = Path.GetFullPath(assemblyDirectory);
            if (!Directory.Exists(normalizedAssemblyDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"No existe el directorio del ensamblaje '{assemblyDirectory}'.");
            }

            var sourcePath = Path.GetFullPath(source.FullPath);
            ValidateSource(source, sourcePath);
            var fullHash = ComputeSha256(sourcePath);
            var cacheRoot = Path.Combine(
                normalizedAssemblyDirectory,
                HardwareFolderName,
                CacheFolderName);

            for (var prefixLength = InitialPrefixLength;
                 prefixLength <= fullHash.Length;
                 prefixLength += PrefixGrowth)
            {
                var actualLength = Math.Min(prefixLength, fullHash.Length);
                var slotDirectory = Path.Combine(cacheRoot, fullHash.Substring(0, actualLength));
                Directory.CreateDirectory(slotDirectory);

                using (AcquireSlotLock(slotDirectory))
                {
                    CleanupStaleTemporaries(slotDirectory);
                    if (!TryClaimSlot(slotDirectory, fullHash))
                    {
                        if (actualLength == fullHash.Length) break;
                        continue;
                    }

                    var copy = TryEnsurePayload(
                        normalizedAssemblyDirectory,
                        slotDirectory,
                        source,
                        sourcePath,
                        fullHash);
                    if (copy != null)
                    {
                        return copy;
                    }
                }

                if (actualLength == fullHash.Length) break;
            }

            throw new InvalidDataException(
                $"No se encontró un prefijo SHA-256 libre y verificable para '{source.Filename}'.");
        }

        private static void ValidateSource(LibraryItem source, string sourcePath)
        {
            var fileName = Path.GetFileName(sourcePath);
            if (!string.Equals(fileName, source.Filename, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "El nombre del elemento no coincide con la ruta del archivo maestro.",
                    nameof(source));
            }
            if (!string.Equals(Path.GetExtension(fileName), ".par", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("La caché solo admite archivos .par.", nameof(source));
            }
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("No se encuentra el archivo maestro de hardware.", sourcePath);
            }
        }

        private static FileStream AcquireSlotLock(string slotDirectory)
        {
            var lockPath = Path.Combine(slotDirectory, SlotLockFileName);
            var deadline = DateTime.UtcNow.AddMilliseconds(LockTimeoutMilliseconds);
            while (true)
            {
                try
                {
                    return new FileStream(
                        lockPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None);
                }
                catch (IOException ex)
                {
                    if (DateTime.UtcNow >= deadline)
                    {
                        throw new TimeoutException($"No se pudo adquirir el bloqueo de caché '{lockPath}'.", ex);
                    }
                    Thread.Sleep(LockRetryMilliseconds);
                }
                catch (UnauthorizedAccessException ex)
                {
                    if (DateTime.UtcNow >= deadline)
                    {
                        throw new TimeoutException($"No se pudo adquirir el bloqueo de caché '{lockPath}'.", ex);
                    }
                    Thread.Sleep(LockRetryMilliseconds);
                }
            }
        }

        private static bool TryClaimSlot(string slotDirectory, string fullHash)
        {
            var hashPath = Path.Combine(slotDirectory, SlotHashFileName);
            if (File.Exists(hashPath))
            {
                return string.Equals(
                    File.ReadAllText(hashPath).Trim(),
                    fullHash,
                    StringComparison.OrdinalIgnoreCase);
            }

            var hasUnownedContent = Directory.EnumerateFileSystemEntries(slotDirectory)
                .Select(Path.GetFileName)
                .Any(name =>
                    !string.Equals(name, SlotLockFileName, StringComparison.OrdinalIgnoreCase) &&
                    !(name?.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ?? false));
            if (hasUnownedContent)
            {
                return false;
            }

            var tempPath = Path.Combine(slotDirectory, ".hash-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                WriteTextAndFlush(tempPath, fullHash + Environment.NewLine);
                File.Move(tempPath, hashPath);
                return true;
            }
            finally
            {
                DeleteIfExists(tempPath);
            }
        }

        private static void CleanupStaleTemporaries(string slotDirectory)
        {
            foreach (var pattern in new[] { ".hash-*.tmp", ".copy-*.tmp", ".manifest-*.tmp" })
            {
                string[] candidates;
                try
                {
                    candidates = Directory.GetFiles(slotDirectory, pattern, SearchOption.TopDirectoryOnly);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var candidate in candidates)
                {
                    TryDeleteClosedTemporary(candidate);
                }
            }
        }

        private static void TryDeleteClosedTemporary(string filePath)
        {
            try
            {
                using (new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    // La apertura exclusiva confirma que no hay un escritor cooperativo activo.
                }
                File.Delete(filePath);
            }
            catch (IOException)
            {
                // Residuo abierto o carrera de filesystem: se conserva para el siguiente acceso.
            }
            catch (UnauthorizedAccessException)
            {
                // Limpieza best-effort; un fallo aquí no debe ocultar la operación solicitada.
            }
        }

        private static LocalHardwareCopy? TryEnsurePayload(
            string assemblyDirectory,
            string slotDirectory,
            LibraryItem source,
            string sourcePath,
            string fullHash)
        {
            var localPath = Path.Combine(slotDirectory, source.Filename);
            var localRelativePath = PathHelper.GetRelativePath(assemblyDirectory, localPath);
            var manifestPath = Path.Combine(slotDirectory, source.Filename + ".manifest.json");
            var manifest = TryReadManifest(manifestPath);

            if (File.Exists(localPath))
            {
                if (!string.Equals(ComputeSha256(localPath), fullHash, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (manifest != null &&
                    IsReusableManifest(manifest, assemblyDirectory, localPath, localRelativePath, fullHash))
                {
                    return manifest.ResolveAt(localPath, localRelativePath);
                }

                var rebuiltManifest = CreateCopy(
                    source,
                    sourcePath,
                    localPath,
                    localRelativePath,
                    fullHash);
                WriteManifestAtomically(rebuiltManifest, manifestPath, replaceExisting: File.Exists(manifestPath));
                return rebuiltManifest;
            }

            return CreatePayloadAndManifest(
                source,
                sourcePath,
                localPath,
                localRelativePath,
                manifestPath,
                fullHash);
        }

        private static bool IsReusableManifest(
            LocalHardwareCopy manifest,
            string assemblyDirectory,
            string expectedLocalPath,
            string expectedRelativePath,
            string fullHash)
        {
            try
            {
                var resolvedPath = Path.GetFullPath(Path.Combine(assemblyDirectory, manifest.LocalRelativePath));
                return string.Equals(manifest.Sha256, fullHash, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(resolvedPath, Path.GetFullPath(expectedLocalPath), StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(manifest.LocalRelativePath, expectedRelativePath, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is ArgumentException ||
                ex is NotSupportedException)
            {
                return false;
            }
        }

        private static LocalHardwareCopy CreatePayloadAndManifest(
            LibraryItem source,
            string sourcePath,
            string localPath,
            string localRelativePath,
            string manifestPath,
            string fullHash)
        {
            var slotDirectory = Path.GetDirectoryName(localPath)
                ?? throw new InvalidOperationException("La ruta local no tiene directorio.");
            var copyTempPath = Path.Combine(slotDirectory, ".copy-" + Guid.NewGuid().ToString("N") + ".tmp");
            var localFileCreated = false;
            try
            {
                CopyAndFlush(sourcePath, copyTempPath);
                if (!string.Equals(ComputeSha256(copyTempPath), fullHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("El archivo maestro cambió mientras se copiaba; no se creó la entrada de caché.");
                }

                File.Move(copyTempPath, localPath);
                localFileCreated = true;
                var result = CreateCopy(source, sourcePath, localPath, localRelativePath, fullHash);
                WriteManifestAtomically(result, manifestPath, replaceExisting: File.Exists(manifestPath));
                return result;
            }
            catch
            {
                DeleteIfExists(copyTempPath);
                if (localFileCreated)
                {
                    DeleteIfExists(localPath);
                }
                throw;
            }
            finally
            {
                DeleteIfExists(copyTempPath);
            }
        }

        private static LocalHardwareCopy CreateCopy(
            LibraryItem source,
            string sourcePath,
            string localPath,
            string localRelativePath,
            string fullHash)
        {
            return new LocalHardwareCopy(
                fullHash,
                sourcePath,
                source.RelativePath,
                localRelativePath,
                localPath,
                DateTime.UtcNow);
        }

        private static void CopyAndFlush(string sourcePath, string destinationPath)
        {
            using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(destination);
                destination.Flush(flushToDisk: true);
            }
        }

        private static void WriteManifestAtomically(
            LocalHardwareCopy copy,
            string manifestPath,
            bool replaceExisting)
        {
            var directory = Path.GetDirectoryName(manifestPath)
                ?? throw new InvalidOperationException("La ruta del manifiesto no tiene directorio.");
            var tempPath = Path.Combine(directory, ".manifest-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    CreateSerializer().WriteObject(stream, copy);
                    stream.Flush(flushToDisk: true);
                }

                if (replaceExisting)
                {
                    File.Replace(tempPath, manifestPath, null);
                }
                else
                {
                    File.Move(tempPath, manifestPath);
                }
            }
            finally
            {
                DeleteIfExists(tempPath);
            }
        }

        private static void WriteTextAndFlush(string destinationPath, string content)
        {
            using (var stream = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
        }

        private static LocalHardwareCopy? TryReadManifest(string manifestPath)
        {
            if (!File.Exists(manifestPath)) return null;

            try
            {
                using (var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    return CreateSerializer().ReadObject(stream) as LocalHardwareCopy;
                }
            }
            catch (SerializationException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static DataContractJsonSerializer CreateSerializer() =>
            new DataContractJsonSerializer(typeof(LocalHardwareCopy));

        private static string ComputeSha256(string filePath)
        {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sha256 = SHA256.Create())
            {
                var bytes = sha256.ComputeHash(stream);
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (var value in bytes)
                {
                    builder.Append(value.ToString("x2"));
                }
                return builder.ToString();
            }
        }

        private static void DeleteIfExists(string filePath)
        {
            try
            {
                if (File.Exists(filePath)) File.Delete(filePath);
            }
            catch (IOException)
            {
                // La limpieza es best-effort y nunca debe ocultar el error original.
            }
            catch (UnauthorizedAccessException)
            {
                // La limpieza es best-effort y nunca debe ocultar el error original.
            }
        }
    }
}
