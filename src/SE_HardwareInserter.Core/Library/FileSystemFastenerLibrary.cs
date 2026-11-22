using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace SE_HardwareInserter.Core.Library
{
    /// <summary>
    /// Implementación de <see cref="IHardwareLibrary"/> que descubre los <c>.par</c> recorriendo
    /// recursivamente una carpeta raíz y mantiene un snapshot mediante <see cref="FileSystemWatcher"/>.
    /// </summary>
    public sealed class FileSystemFastenerLibrary : IHardwareLibrary
    {
        private const string RootCategoryLabel = "(Raíz)";
        private const int DebounceMilliseconds = 175;

        private readonly object _stateSync = new object();
        private readonly object _scanSync = new object();
        private readonly Timer _debounceTimer;
        private readonly Func<string, List<LibraryItem>> _scanner;
        private FileSystemWatcher? _watcher;
        private List<LibraryItem> _items = new List<LibraryItem>();
        private string _rootPath;
        private long _generation;
        private long _scheduledGeneration;
        private Exception? _lastError;
        private bool _disposed;

        /// <summary>
        /// Construye la librería apuntando a <paramref name="rootPath"/> y carga su contenido
        /// antes de retornar. Si la ruta no existe, lanza <see cref="DirectoryNotFoundException"/>.
        /// </summary>
        public FileSystemFastenerLibrary(string rootPath)
            : this(rootPath, Scan)
        {
        }

        internal FileSystemFastenerLibrary(string rootPath, Func<string, List<LibraryItem>> scanner)
        {
            _rootPath = NormalizeExistingRootPath(rootPath);
            _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
            _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);

            try
            {
                _watcher = CreateWatcher(_rootPath);
                _watcher.EnableRaisingEvents = true;
                RefreshGeneration(_generation, throwOnError: true);
            }
            catch
            {
                DisposeWatcher(_watcher);
                _watcher = null;
                _debounceTimer.Dispose();
                throw;
            }
        }

        /// <inheritdoc />
        public string RootPath
        {
            get
            {
                lock (_stateSync)
                {
                    return _rootPath;
                }
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<LibraryItem> Items
        {
            get
            {
                lock (_stateSync)
                {
                    return _items.ToList();
                }
            }
        }

        /// <summary>Último error recuperable de observación o escaneo; null tras un escaneo correcto.</summary>
        public Exception? LastError
        {
            get
            {
                lock (_stateSync)
                {
                    return _lastError;
                }
            }
        }

        /// <inheritdoc />
        public event EventHandler? Changed;

        /// <summary>Se lanza cuando el watcher o un escaneo no pueden actualizar el snapshot.</summary>
        public event EventHandler<LibraryErrorEventArgs>? Error;

        /// <inheritdoc />
        public void Refresh()
        {
            long generation;
            lock (_stateSync)
            {
                ThrowIfDisposed();
                generation = _generation;
            }

            RefreshGeneration(generation, throwOnError: false);
        }

        /// <inheritdoc />
        public void SetRootPath(string rootPath)
        {
            var normalizedRoot = NormalizeExistingRootPath(rootPath);
            var validatedSnapshot = _scanner(normalizedRoot);
            var newWatcher = CreateWatcher(normalizedRoot);

            try
            {
                newWatcher.EnableRaisingEvents = true;
            }
            catch
            {
                DisposeWatcher(newWatcher);
                throw;
            }

            FileSystemWatcher? previousWatcher = null;
            bool changed;
            long generation;
            try
            {
                lock (_scanSync)
                {
                    lock (_stateSync)
                    {
                        ThrowIfDisposed();
                        previousWatcher = _watcher;
                        _watcher = newWatcher;
                        _rootPath = normalizedRoot;
                        generation = ++_generation;
                        _scheduledGeneration = generation;
                        _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
                        changed = !SequenceEqual(_items, validatedSnapshot);
                        _items = validatedSnapshot;
                        _lastError = null;
                    }
                }
            }
            catch
            {
                DisposeWatcher(newWatcher);
                throw;
            }

            DisposeWatcher(previousWatcher);
            if (changed)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }

            // El segundo escaneo cierra el intervalo entre la validación y la activación del watcher.
            RefreshGeneration(generation, throwOnError: false);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            FileSystemWatcher? watcher;
            lock (_scanSync)
            {
                lock (_stateSync)
                {
                    if (_disposed) return;
                    _disposed = true;
                    _generation++;
                    _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    watcher = _watcher;
                    _watcher = null;
                }
            }

            DisposeWatcher(watcher);
            _debounceTimer.Dispose();
        }

        internal long CaptureGeneration()
        {
            lock (_stateSync)
            {
                ThrowIfDisposed();
                return _generation;
            }
        }

        internal void RefreshGeneration(long generation)
        {
            RefreshGeneration(generation, throwOnError: false);
        }

        private static string NormalizeExistingRootPath(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                throw new ArgumentException("La carpeta raíz de la librería no puede estar vacía.", nameof(rootPath));
            }

            var normalizedRoot = Path.GetFullPath(rootPath);
            if (!Directory.Exists(normalizedRoot))
            {
                throw new DirectoryNotFoundException($"No existe la carpeta de librería '{rootPath}'.");
            }

            return normalizedRoot;
        }

        private FileSystemWatcher CreateWatcher(string rootPath)
        {
            var watcher = new FileSystemWatcher(rootPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.CreationTime,
                EnableRaisingEvents = false,
            };
            watcher.Created += OnWatcherEvent;
            watcher.Deleted += OnWatcherEvent;
            watcher.Renamed += OnWatcherEvent;
            watcher.Error += OnWatcherError;
            return watcher;
        }

        private void OnWatcherEvent(object sender, FileSystemEventArgs e)
        {
            lock (_stateSync)
            {
                if (_disposed) return;
                _scheduledGeneration = _generation;
                _debounceTimer.Change(DebounceMilliseconds, Timeout.Infinite);
            }
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            PublishError(RootPath, e.GetException());
            ScheduleRefresh();
        }

        private void ScheduleRefresh()
        {
            lock (_stateSync)
            {
                if (_disposed) return;
                _scheduledGeneration = _generation;
                _debounceTimer.Change(DebounceMilliseconds, Timeout.Infinite);
            }
        }

        private void OnDebounceElapsed(object? state)
        {
            long generation;
            lock (_stateSync)
            {
                if (_disposed) return;
                generation = _scheduledGeneration;
            }

            RefreshGeneration(generation, throwOnError: false);
        }

        private void RefreshGeneration(long generation, bool throwOnError)
        {
            List<LibraryItem> snapshot;
            string rootPath;
            bool changed;

            lock (_scanSync)
            {
                lock (_stateSync)
                {
                    if (_disposed || generation != _generation) return;
                    rootPath = _rootPath;
                }

                try
                {
                    snapshot = _scanner(rootPath);
                }
                catch (Exception ex) when (IsRecoverableScanError(ex))
                {
                    if (throwOnError) throw;
                    PublishError(rootPath, ex);
                    return;
                }

                lock (_stateSync)
                {
                    if (_disposed || generation != _generation) return;
                    changed = !SequenceEqual(_items, snapshot);
                    if (changed)
                    {
                        _items = snapshot;
                    }
                    _lastError = null;
                }
            }

            if (changed)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        private void PublishError(string rootPath, Exception exception)
        {
            lock (_stateSync)
            {
                if (_disposed) return;
                _lastError = exception;
            }

            Error?.Invoke(this, new LibraryErrorEventArgs(rootPath, exception));
        }

        private static bool IsRecoverableScanError(Exception exception) =>
            exception is IOException || exception is UnauthorizedAccessException;

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(FileSystemFastenerLibrary));
            }
        }

        private static void DisposeWatcher(FileSystemWatcher? watcher)
        {
            watcher?.Dispose();
        }

        private static List<LibraryItem> Scan(string rootPath)
        {
            var root = Path.GetFullPath(rootPath);
            var rootDir = new DirectoryInfo(root);
            var items = new List<LibraryItem>();

            foreach (var file in rootDir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                if (!string.Equals(file.Extension, ".par", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relativePath = PathHelper.GetRelativePath(root, file.FullName);
                var category = ResolveCategory(root, file);
                items.Add(new LibraryItem(file.FullName, relativePath, category, file.Name));
            }

            items.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));
            return items;
        }

        private static string ResolveCategory(string root, FileInfo file)
        {
            var relativePath = PathHelper.GetRelativePath(root, file.DirectoryName ?? root);
            if (string.IsNullOrEmpty(relativePath) || relativePath == ".")
            {
                return RootCategoryLabel;
            }

            var segments = relativePath.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            return segments[0];
        }

        private static bool SequenceEqual(List<LibraryItem> a, List<LibraryItem> b)
        {
            if (a.Count != b.Count) return false;
            for (var i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i].FullPath, b[i].FullPath, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }
    }
}
