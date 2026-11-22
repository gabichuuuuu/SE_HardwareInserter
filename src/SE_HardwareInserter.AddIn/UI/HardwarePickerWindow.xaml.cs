using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SE_HardwareInserter.Core.Diagnostics;
using SE_HardwareInserter.Core.Library;
using SE_HardwareInserter.Core.Preferences;
using SE_HardwareInserter.SolidEdge;
using SolidEdgeFramework;

namespace SE_HardwareInserter.AddIn.UI
{
    /// <summary>
    /// Diálogo WPF no modal del add-in. Muestra el árbol de la librería de hardware (archivos
    /// <c>.par</c> descubiertos en la carpeta raíz configurada), permite capturar la geometría del
    /// agujero seleccionado en Solid Edge e insertar el hardware elegido como occurrence del
    /// ensamblaje activo, con o sin restricciones según el checkbox.
    /// </summary>
    public partial class HardwarePickerWindow : System.Windows.Window
    {
        private readonly SolidEdgeFramework.Application _solidEdgeApp;
        private readonly SolidEdgeHelper _solidEdgeHelper;
        private readonly LocalHardwareCache _localHardwareCache;
        private readonly SettingsStore _settingsStore;
        private readonly IAppLogger _logger;
        private readonly object _libraryErrorSync = new object();
        private IHardwareLibrary _library;
        private IHardwareLibrary? _pendingLibraryErrorSource;
        private Exception? _pendingLibraryError;
        private CapturedHoleGeometry? _capturedHole;
        private InsertionSession? _pendingInsertion;
        private ISEApplicationEvents_Event? _applicationEvents;
        private bool _busy;
        private bool _copyInProgress;
        private bool _forceClosing;
        private bool _pendingCleanupOnly;
        private string _filter = string.Empty;
        private string? _selectedRelativePath;

        /// <summary>Construye el diálogo con la instancia de Solid Edge y el helper de inserción.</summary>
        public HardwarePickerWindow(SolidEdgeFramework.Application solidEdgeApp, SolidEdgeHelper solidEdgeHelper)
            : this(solidEdgeApp, solidEdgeHelper, NullAppLogger.Instance)
        {
        }

        public HardwarePickerWindow(
            SolidEdgeFramework.Application solidEdgeApp,
            SolidEdgeHelper solidEdgeHelper,
            IAppLogger logger)
        {
            _solidEdgeApp = solidEdgeApp ?? throw new ArgumentNullException(nameof(solidEdgeApp));
            _solidEdgeHelper = solidEdgeHelper ?? throw new ArgumentNullException(nameof(solidEdgeHelper));
            _localHardwareCache = new LocalHardwareCache();
            _settingsStore = new SettingsStore();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            InitializeComponent();

            var settings = _settingsStore.Load();
            if (!string.IsNullOrWhiteSpace(settings.RootPath) && Directory.Exists(settings.RootPath))
            {
                _library = new FileSystemFastenerLibrary(settings.RootPath);
            }
            else
            {
                _library = new EmptyLibrary();
            }
            RootPathBox.Text = _library.RootPath;

            SubscribeLibrary(_library);
            SubscribeApplicationEvents();
            RefreshTree();
            UpdateUiState();
        }

        private void SubscribeLibrary(IHardwareLibrary library)
        {
            library.Changed += OnLibraryChanged;
            library.Error += OnLibraryError;

            var lastError = library.LastError;
            if (lastError != null)
            {
                QueueLibraryError(library, lastError);
            }
        }

        private void UnsubscribeLibrary(IHardwareLibrary library)
        {
            library.Changed -= OnLibraryChanged;
            library.Error -= OnLibraryError;
        }

        private void OnLibraryChanged(object sender, EventArgs e)
        {
            // El FileSystemWatcher dispara en un hilo de pool; marshalar al hilo UI de WPF.
            Dispatcher.BeginInvoke(new Action(RefreshTree));
        }

        private void OnLibraryError(object sender, LibraryErrorEventArgs e)
        {
            QueueLibraryError(sender as IHardwareLibrary ?? _library, e.Exception);
        }

        private void QueueLibraryError(IHardwareLibrary source, Exception exception)
        {
            lock (_libraryErrorSync)
            {
                if (ReferenceEquals(_pendingLibraryErrorSource, source)
                    && ReferenceEquals(_pendingLibraryError, exception))
                {
                    return;
                }

                _pendingLibraryErrorSource = source;
                _pendingLibraryError = exception;
            }

            // Los fallos del watcher son recuperables: conservar el snapshot y notificar sin modal.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                lock (_libraryErrorSync)
                {
                    if (!ReferenceEquals(_pendingLibraryErrorSource, source)
                        || !ReferenceEquals(_pendingLibraryError, exception))
                    {
                        return;
                    }

                    _pendingLibraryErrorSource = null;
                    _pendingLibraryError = null;
                }

                if (ReferenceEquals(_library, source))
                {
                    StatusText.Text = "No se pudo actualizar la librería: " + exception.Message;
                }
            }));
        }

        private void RefreshTree()
        {
            var hadPreviousTree = LibraryTree.ItemsSource is IEnumerable<LibraryTreeNode>;
            var expandedFolders = CaptureExpandedFolders();
            var items = _library.Items;
            var filtered = string.IsNullOrEmpty(_filter)
                ? items
                : items.Where(i => i.RelativePath.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            var root = new LibraryFolderBuilder(string.Empty);
            foreach (var item in filtered)
            {
                var segments = item.RelativePath.Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);
                var folder = root;
                for (var index = 0; index < segments.Length - 1; index++)
                {
                    folder = folder.GetOrAddFolder(segments[index]);
                }
                folder.AddItem(item);
            }

            LibraryTree.ItemsSource = root.CreateChildren(
                _selectedRelativePath,
                expandedFolders,
                expandAll: !hadPreviousTree);
            UpdateUiState();
        }

        private HashSet<string> CaptureExpandedFolders()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (LibraryTree.ItemsSource is IEnumerable<LibraryTreeNode> roots)
            {
                CaptureExpandedFolders(roots, paths);
            }
            return paths;
        }

        private static void CaptureExpandedFolders(
            IEnumerable<LibraryTreeNode> nodes,
            ISet<string> paths)
        {
            foreach (var category in nodes.OfType<CategoryNode>())
            {
                if (category.IsExpanded) paths.Add(category.RelativePath);
                CaptureExpandedFolders(category.Children, paths);
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Seleccione la carpeta raíz de la librería de hardware (.par)",
                ShowNewFolderButton = false,
            })
            {
                if (!string.IsNullOrWhiteSpace(_library.RootPath) && Directory.Exists(_library.RootPath))
                {
                    dialog.SelectedPath = _library.RootPath;
                }
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

                var newRoot = dialog.SelectedPath;
                try
                {
                    if (_library is FileSystemFastenerLibrary fs)
                    {
                        fs.SetRootPath(newRoot);
                    }
                    else
                    {
                        var newLibrary = new FileSystemFastenerLibrary(newRoot);
                        UnsubscribeLibrary(_library);
                        _library.Dispose();
                        _library = newLibrary;
                        SubscribeLibrary(_library);
                        RefreshTree();
                    }
                    RootPathBox.Text = newRoot;

                    // Persistir la preferencia.
                    var settings = new Settings { RootPath = newRoot };
                    _settingsStore.Save(settings);
                }
                catch (Exception ex)
                {
                    StatusText.Text = "Error al cambiar carpeta: " + ex.Message;
                }
            }
        }

        private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _filter = FilterBox.Text ?? string.Empty;
            RefreshTree();
        }

        private void LibraryTree_SelectedItemChanged(
            object sender,
            RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is LibraryItemNode itemNode)
            {
                _selectedRelativePath = itemNode.Item.RelativePath;
            }
            UpdateUiState();
        }

        private void ApplyConstraintsCheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateUiState();
        }

        private void CaptureHoleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_busy || _pendingInsertion != null) return;

            CapturedHoleGeometry? replacement = null;
            SetBusy(true);
            try
            {
                var capturedHole = _solidEdgeHelper.CaptureSelectedHoleGeometry(_solidEdgeApp);
                replacement = capturedHole;
                var previous = _capturedHole;
                _capturedHole = capturedHole;
                replacement = null;
                previous?.Dispose();

                var hasSupport = capturedHole.SupportReference != null;
                HoleInfo.Text = hasSupport
                    ? $"Agujero: Ø{capturedHole.DiameterMm:0.##} mm con apoyo"
                    : $"Agujero: Ø{capturedHole.DiameterMm:0.##} mm sin apoyo";
                StatusText.Text = hasSupport
                    ? "Agujero y cara de apoyo capturados."
                    : "Captura incompleta para restringir: seleccione también una cara plana de apoyo.";
            }
            catch (Exception ex)
            {
                StatusText.Text = _capturedHole == null
                    ? "Error al capturar: " + ex.Message
                    : "Error al capturar; se conserva la captura anterior: " + ex.Message;
            }
            finally
            {
                replacement?.Dispose();
                SetBusy(false);
            }
        }

        private void LibraryTree_MouseDoubleClick(object sender, RoutedEventArgs e)
        {
            if (InsertButton.IsEnabled)
            {
                InsertButton_Click(sender, e);
            }
        }

        private async void InsertButton_Click(object sender, RoutedEventArgs e)
        {
            if (_busy || _pendingInsertion != null) return;

            var item = GetSelectedLibraryItem();
            if (item == null)
            {
                StatusText.Text = "Seleccione un archivo .par del árbol.";
                return;
            }

            var applyConstraints = ApplyConstraintsCheckBox.IsChecked ?? true;
            var capturedHole = _capturedHole;
            if (applyConstraints && (capturedHole == null || capturedHole.SupportReference == null))
            {
                StatusText.Text =
                    "Para aplicar restricciones, capture la cara cilíndrica del agujero y una cara plana de apoyo.";
                return;
            }

            var operationId = Guid.NewGuid().ToString("N");
            var assemblyFullName = "unavailable";
            var sha256 = "unavailable";
            SetBusy(true);
            try
            {
                var assemblyIdentity = SolidEdgeHelper.GetActiveAssemblyIdentity(_solidEdgeApp);
                var assemblyDirectory = assemblyIdentity.Directory;
                assemblyFullName = assemblyIdentity.FullName;

                StatusText.Text = "Copiando y verificando el archivo de hardware...";
                LogInfo(operationId, "copy", "Comienza la copia local de hardware.", assemblyFullName, sha256);
                _copyInProgress = true;
                LocalHardwareCopy localCopy;
                try
                {
                    localCopy = await Task.Run(() =>
                        _localHardwareCache.EnsureLocalCopy(assemblyDirectory, item));
                }
                finally
                {
                    _copyInProgress = false;
                }
                sha256 = localCopy.Sha256;
                LogInfo(operationId, "copy", "Copia local verificada.", assemblyFullName, sha256);

                if (_forceClosing || !IsLoaded)
                {
                    LogInfo(
                        operationId,
                        "copy",
                        "La ventana se cerró durante la copia; no se iniciará ninguna operación COM.",
                        assemblyFullName,
                        sha256);
                    return;
                }

                var currentAssemblyIdentity = SolidEdgeHelper.GetActiveAssemblyIdentity(_solidEdgeApp);
                if (!string.Equals(
                        currentAssemblyIdentity.Directory,
                        assemblyIdentity.Directory,
                        StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(
                        currentAssemblyIdentity.FullName,
                        assemblyIdentity.FullName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "El ensamblaje activo cambió durante la copia. No se insertó ningún elemento; vuelva a intentarlo en el ensamblaje actual.");
                }

                var localParPath = localCopy.LocalPath;

                if (!applyConstraints)
                {
                    var freeSession = _solidEdgeHelper.BeginInsertion(
                        _solidEdgeApp,
                        localParPath,
                        operationId);
                    _pendingInsertion = freeSession;
                    _pendingCleanupOnly = false;
                    freeSession.CommitFree();
                    CompletePendingInsertion();
                    StatusText.Text = $"Insertado libremente: {item.Filename}.";
                    return;
                }

                if (capturedHole == null)
                {
                    throw new InvalidOperationException("La captura del agujero ya no está disponible.");
                }

                var pendingInsertion = _solidEdgeHelper.BeginInsertion(
                    _solidEdgeApp,
                    localParPath,
                    operationId);
                _pendingInsertion = pendingInsertion;
                _pendingCleanupOnly = false;
                var resolution = pendingInsertion.TryApplyAutomaticConstraints(capturedHole);
                if (resolution == AutomaticResolution.Resolved)
                {
                    CompletePendingInsertion();
                    StatusText.Text = $"Insertado con restricciones: {item.Filename}.";
                    return;
                }

                var resolutionLabel = resolution == AutomaticResolution.Ambiguous
                    ? "La geometría del hardware es ambigua."
                    : "La geometría del hardware no se pudo resolver automáticamente.";
                var details = pendingInsertion.LastAutomaticResolutionMessage;
                GuidedInstructionsText.Text =
                    resolutionLabel + " Seleccione en Solid Edge primero la cara cilíndrica del vástago " +
                    "y después la cara plana de apoyo; luego pulse 'Usar caras seleccionadas'.";
                StatusText.Text = string.IsNullOrWhiteSpace(details)
                    ? resolutionLabel + " La inserción provisional se conserva para selección guiada."
                    : resolutionLabel + " " + details + " La inserción provisional se conserva para selección guiada.";
            }
            catch (Exception ex)
            {
                LogError(operationId, "insert", "Falló la operación de inserción.", ex, assemblyFullName, sha256);
                HandlePendingInsertionFailure(
                    ex,
                    "Error al insertar",
                    assemblyFullName,
                    sha256);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void UseSelectedFacesButton_Click(object sender, RoutedEventArgs e)
        {
            var pendingInsertion = _pendingInsertion;
            if (_busy || pendingInsertion == null) return;

            SetBusy(true);
            GuidedScrewSelection? selection = null;
            try
            {
                try
                {
                    selection = GuidedScrewSelection.Capture(_solidEdgeApp, pendingInsertion);
                }
                catch (Exception ex)
                {
                    StatusText.Text =
                        "Selección guiada no válida: " + ex.Message + " La inserción provisional sigue pendiente.";
                    return;
                }

                try
                {
                    pendingInsertion.ApplyGuidedConstraints(
                        _capturedHole
                            ?? throw new InvalidOperationException("La captura del agujero ya no está disponible."),
                        selection.AxisReference,
                        selection.SupportReference);
                    CompletePendingInsertion();
                    StatusText.Text = "Inserción confirmada con las caras seleccionadas.";
                }
                catch (Exception ex)
                {
                    HandlePendingInsertionFailure(
                        ex,
                        "No se pudieron aplicar las restricciones",
                        pendingInsertion.AssemblyFullName,
                        "unavailable");
                }
            }
            finally
            {
                selection?.Dispose();
                SetBusy(false);
            }
        }

        private void KeepFreeButton_Click(object sender, RoutedEventArgs e)
        {
            var pendingInsertion = _pendingInsertion;
            if (_busy || pendingInsertion == null) return;

            SetBusy(true);
            try
            {
                pendingInsertion.CommitFree();
                CompletePendingInsertion();
                StatusText.Text = "Inserción provisional conservada como pieza libre.";
            }
            catch (Exception ex)
            {
                HandlePendingInsertionFailure(
                    ex,
                    "No se pudo conservar la inserción libre",
                    pendingInsertion.AssemblyFullName,
                    "unavailable");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void CancelPendingButton_Click(object sender, RoutedEventArgs e)
        {
            var pendingInsertion = _pendingInsertion;
            if (_busy || pendingInsertion == null) return;

            SetBusy(true);
            try
            {
                var rollback = pendingInsertion.Rollback();
                if (rollback.CleanupComplete)
                {
                    DisposePendingInsertion();
                    StatusText.Text = "Inserción provisional cancelada y eliminada.";
                }
            }
            catch (Exception ex)
            {
                _pendingCleanupOnly = true;
                GuidedInstructionsText.Text =
                    "La occurrence provisional no pudo eliminarse. Pulse 'Cancelar' para reintentar " +
                    "o elimínela manualmente en Solid Edge antes de cerrar.";
                LogError(
                    pendingInsertion.OperationId,
                    "rollback-incomplete",
                    "La cancelación dejó una inserción pendiente para reintento o limpieza manual.",
                    ex,
                    pendingInsertion.AssemblyFullName,
                    "unavailable");
                StatusText.Text = FormatInsertionError("No se pudo cancelar completamente la inserción", ex);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private LibraryItem? GetSelectedLibraryItem()
        {
            if (LibraryTree.SelectedItem is LibraryItemNode fileNode) return fileNode.Item;
            return null;
        }

        private void CompletePendingInsertion()
        {
            var completed = _pendingInsertion;
            _pendingInsertion = null;
            _pendingCleanupOnly = false;
            completed?.Dispose();
        }

        private void DisposePendingInsertion()
        {
            var pending = _pendingInsertion;
            _pendingInsertion = null;
            _pendingCleanupOnly = false;
            pending?.Dispose();
        }

        private void HandlePendingInsertionFailure(
            Exception exception,
            string messagePrefix,
            string assemblyFullName,
            string sha256)
        {
            var rollback = GetRollbackResult(exception);
            var pendingInsertion = _pendingInsertion;
            if (pendingInsertion != null && rollback != null && !rollback.CleanupComplete)
            {
                _pendingCleanupOnly = true;
                GuidedInstructionsText.Text =
                    "La occurrence provisional sigue en el ensamblaje. Pulse 'Cancelar' para reintentar " +
                    "la limpieza o elimínela manualmente en Solid Edge.";
                LogError(
                    pendingInsertion.OperationId,
                    "cleanup-incomplete",
                    "La occurrence provisional permanece tras un fallo; se conserva la sesión para reintentar el rollback.",
                    exception,
                    assemblyFullName,
                    sha256);
                StatusText.Text = FormatInsertionError(messagePrefix, exception) +
                    " La sesión permanece disponible para reintentar con 'Cancelar'.";
                return;
            }

            DisposePendingInsertion();
            StatusText.Text = FormatInsertionError(messagePrefix, exception);
        }

        private static RollbackResult? GetRollbackResult(Exception exception)
        {
            if (exception is InsertionOperationException operationException)
            {
                return operationException.Rollback;
            }
            if (exception is InsertionRollbackException rollbackException)
            {
                return rollbackException.Result;
            }
            return null;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            UpdateUiState();
        }

        private void UpdateUiState()
        {
            var hasPendingInsertion = _pendingInsertion != null;
            var normalControlsEnabled = !_busy && !hasPendingInsertion;
            var applyConstraints = ApplyConstraintsCheckBox.IsChecked ?? true;
            var capturedHole = _capturedHole;
            var hasCompleteCapture = capturedHole != null && capturedHole.SupportReference != null;

            RootPathBox.IsEnabled = normalControlsEnabled;
            BrowseButton.IsEnabled = normalControlsEnabled;
            FilterBox.IsEnabled = normalControlsEnabled;
            LibraryTree.IsEnabled = normalControlsEnabled;
            CaptureHoleButton.IsEnabled = normalControlsEnabled;
            ApplyConstraintsCheckBox.IsEnabled = normalControlsEnabled;
            InsertButton.IsEnabled = normalControlsEnabled
                && GetSelectedLibraryItem() != null
                && (!applyConstraints || hasCompleteCapture);

            GuidedPanel.Visibility = hasPendingInsertion ? Visibility.Visible : Visibility.Collapsed;
            UseSelectedFacesButton.IsEnabled = !_busy && hasPendingInsertion && !_pendingCleanupOnly;
            KeepFreeButton.IsEnabled = !_busy && hasPendingInsertion && !_pendingCleanupOnly;
            CancelPendingButton.IsEnabled = !_busy && hasPendingInsertion;
        }

        private static string FormatInsertionError(string prefix, Exception exception)
        {
            if (exception is InsertionOperationException operationException)
            {
                var originalMessage = operationException.InnerException?.Message ?? operationException.Message;
                return prefix + ": " + originalMessage +
                    " La limpieza quedó incompleta; revise y elimine cualquier elemento provisional del ensamblaje.";
            }
            if (exception is InsertionRollbackException)
            {
                return prefix + ": " + exception.Message +
                    " Revise y elimine cualquier elemento provisional que permanezca en el ensamblaje.";
            }
            return prefix + ": " + exception.Message;
        }

        /// <summary>Cierra la ventana durante la descarga sin permitir continuación COM tras una copia.</summary>
        public void CloseForDisconnection()
        {
            _forceClosing = true;
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_forceClosing && _copyInProgress)
            {
                e.Cancel = true;
                StatusText.Text = "Espere a que termine la copia antes de cerrar la ventana.";
                base.OnClosing(e);
                return;
            }

            var pendingInsertion = _pendingInsertion;
            if (pendingInsertion != null)
            {
                if (_forceClosing)
                {
                    try
                    {
                        var rollback = pendingInsertion.Rollback();
                        if (rollback.CleanupComplete && pendingInsertion.LastRollbackDiagnostics.Count > 0)
                        {
                            LogInfo(
                                pendingInsertion.OperationId,
                                "rollback-warning",
                                "El rollback se completó con diagnósticos intermedios.",
                                pendingInsertion.AssemblyFullName,
                                "unavailable");
                        }
                    }
                    catch (InsertionRollbackException ex)
                    {
                        LogError(
                            pendingInsertion.OperationId,
                            "rollback-incomplete",
                            "La descarga no pudo completar el rollback de la inserción pendiente.",
                            ex,
                            pendingInsertion.AssemblyFullName,
                            "unavailable");
                    }
                    catch (Exception ex)
                    {
                        LogError(
                            pendingInsertion.OperationId,
                            "rollback",
                            "Falló el rollback durante la descarga.",
                            ex,
                            pendingInsertion.AssemblyFullName,
                            "unavailable");
                    }
                    finally
                    {
                        DisposePendingInsertion();
                    }
                }
                else
                {
                    try
                    {
                        pendingInsertion.Rollback();
                        DisposePendingInsertion();
                    }
                    catch (Exception ex)
                    {
                        _pendingCleanupOnly = true;
                        e.Cancel = true;
                        UpdateUiState();
                        LogError(
                            pendingInsertion.OperationId,
                            "rollback-incomplete",
                            "No se pudo limpiar la inserción pendiente al cerrar.",
                            ex,
                            pendingInsertion.AssemblyFullName,
                            "unavailable");
                        StatusText.Text = FormatInsertionError(
                            "No se puede cerrar porque la limpieza quedó incompleta",
                            ex);
                        MessageBox.Show(
                            this,
                            StatusText.Text,
                            "SE_HardwareInserter",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        base.OnClosing(e);
                        return;
                    }
                }
            }

            base.OnClosing(e);
        }

        private void SubscribeApplicationEvents()
        {
            try
            {
                _applicationEvents = (ISEApplicationEvents_Event)_solidEdgeApp.ApplicationEvents;
                _applicationEvents.BeforeDocumentClose += OnBeforeDocumentClose;
            }
            catch (Exception ex)
            {
                _applicationEvents = null;
                LogError(
                    Guid.NewGuid().ToString("N"),
                    "events",
                    "No se pudo suscribir BeforeDocumentClose; la captura se liberará al cerrar la ventana.",
                    ex,
                    "unavailable",
                    "unavailable");
            }
        }

        private void UnsubscribeApplicationEvents()
        {
            var events = _applicationEvents;
            _applicationEvents = null;
            if (events == null) return;
            try
            {
                events.BeforeDocumentClose -= OnBeforeDocumentClose;
            }
            catch (Exception)
            {
                // Durante la descarga Solid Edge puede haber liberado ya el punto de conexión.
            }
        }

        /// <summary>
        /// Libera la captura y revierte la inserción pendiente antes de que Solid Edge cierre su
        /// documento. Retener esas Reference tras el cierre provoca un fallo de Solid Edge 2026 al
        /// salir («The rollback event cannot allow saving…»), reproducido tres veces.
        /// </summary>
        private void OnBeforeDocumentClose(object theDocument)
        {
            try
            {
                var capturedHole = _capturedHole;
                if (theDocument == null || capturedHole == null || !capturedHole.BelongsTo(theDocument)) return;

                // Una inserción pendiente siempre pertenece al ensamblaje de la captura.
                DisposePendingInsertion();
                _capturedHole = null;
                capturedHole.Dispose();
                HoleInfo.Text = "Agujero: no capturado";
                StatusText.Text = "Se cerró el ensamblaje de la captura; vuelva a capturar el agujero.";
                UpdateUiState();
            }
            catch (Exception ex)
            {
                LogError(
                    Guid.NewGuid().ToString("N"),
                    "document-close",
                    "No se pudo liberar la captura al cerrar su documento.",
                    ex,
                    "unavailable",
                    "unavailable");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            UnsubscribeApplicationEvents();
            DisposePendingInsertion();
            _capturedHole?.Dispose();
            _capturedHole = null;
            UnsubscribeLibrary(_library);
            _library.Dispose();
            base.OnClosed(e);
        }

        private void LogInfo(
            string operationId,
            string phase,
            string message,
            string assemblyFullName,
            string sha256)
        {
            try
            {
                _logger.Info(
                    operationId,
                    phase,
                    message,
                    context: BuildLogContext(null, assemblyFullName, sha256));
            }
            catch (Exception)
            {
                // El diagnóstico inyectado no puede alterar el flujo de la ventana.
            }
        }

        private void LogError(
            string operationId,
            string phase,
            string message,
            Exception? exception,
            string assemblyFullName,
            string sha256)
        {
            try
            {
                _logger.Error(
                    operationId,
                    phase,
                    message,
                    exception,
                    BuildLogContext(exception, assemblyFullName, sha256));
            }
            catch (Exception)
            {
                // El diagnóstico inyectado no puede alterar el flujo de la ventana.
            }
        }

        private IReadOnlyDictionary<string, string> BuildLogContext(
            Exception? exception,
            string assemblyFullName,
            string sha256)
        {
            string solidEdgeVersion;
            try
            {
                solidEdgeVersion = _solidEdgeApp.Version ?? "unavailable";
            }
            catch (Exception)
            {
                solidEdgeVersion = "unavailable";
            }

            return new Dictionary<string, string>
            {
                ["document"] = assemblyFullName,
                ["hresult"] = exception == null ? "unavailable" : exception.HResult.ToString(),
                ["sha256"] = sha256,
                ["solidEdgeVersion"] = solidEdgeVersion,
            };
        }
    }

    /// <summary>Base enlazable para conservar selección y expansión al reconstruir el árbol.</summary>
    public abstract class LibraryTreeNode
    {
        public bool IsSelected { get; set; }
        public bool IsExpanded { get; set; }
    }

    /// <summary>Nodo de categoría recursiva en el árbol.</summary>
    public sealed class CategoryNode : LibraryTreeNode
    {
        public CategoryNode(
            string name,
            string relativePath,
            IReadOnlyList<LibraryTreeNode> children,
            bool isExpanded)
        {
            Name = name;
            RelativePath = relativePath;
            Children = children;
            IsExpanded = isExpanded;
        }
        public string Name { get; }
        public string RelativePath { get; }
        public IReadOnlyList<LibraryTreeNode> Children { get; }
    }

    /// <summary>Nodo hoja del árbol: un archivo .par concreto.</summary>
    public sealed class LibraryItemNode : LibraryTreeNode
    {
        public LibraryItemNode(LibraryItem item, bool isSelected)
        {
            Item = item;
            Label = item.Filename;
            ToolTip = item.RelativePath;
            IsSelected = isSelected;
        }
        public LibraryItem Item { get; }
        public string Label { get; }
        public string ToolTip { get; }
    }

    internal sealed class LibraryFolderBuilder
    {
        private readonly Dictionary<string, LibraryFolderBuilder> _folders =
            new Dictionary<string, LibraryFolderBuilder>(StringComparer.OrdinalIgnoreCase);
        private readonly List<LibraryItem> _items = new List<LibraryItem>();

        public LibraryFolderBuilder(string name)
            : this(name, string.Empty)
        {
        }

        private LibraryFolderBuilder(string name, string relativePath)
        {
            Name = name;
            RelativePath = relativePath;
        }

        public string Name { get; }
        public string RelativePath { get; }

        public LibraryFolderBuilder GetOrAddFolder(string name)
        {
            if (!_folders.TryGetValue(name, out var folder))
            {
                var relativePath = string.IsNullOrEmpty(RelativePath)
                    ? name
                    : Path.Combine(RelativePath, name);
                folder = new LibraryFolderBuilder(name, relativePath);
                _folders.Add(name, folder);
            }
            return folder;
        }

        public void AddItem(LibraryItem item)
        {
            _items.Add(item);
        }

        public IReadOnlyList<LibraryTreeNode> CreateChildren(
            string? selectedRelativePath,
            ISet<string> expandedFolders,
            bool expandAll)
        {
            var children = new List<LibraryTreeNode>();
            children.AddRange(_folders.Values
                .OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
                .Select(folder => new CategoryNode(
                    folder.Name,
                    folder.RelativePath,
                    folder.CreateChildren(selectedRelativePath, expandedFolders, expandAll),
                    expandAll || expandedFolders.Contains(folder.RelativePath))));
            children.AddRange(_items
                .OrderBy(item => item.Filename, StringComparer.OrdinalIgnoreCase)
                .Select(item => new LibraryItemNode(
                    item,
                    string.Equals(
                        item.RelativePath,
                        selectedRelativePath,
                        StringComparison.OrdinalIgnoreCase))));
            return children;
        }
    }

    /// <summary>
    /// Implementación vacía de <see cref="IHardwareLibrary"/> para cuando el usuario aún no ha
    /// configurado una carpeta raíz válida. Evita que el diálogo falle al arrancar sin configuración.
    /// </summary>
    internal sealed class EmptyLibrary : IHardwareLibrary
    {
        public string RootPath => string.Empty;
        public IReadOnlyList<LibraryItem> Items => new List<LibraryItem>();
        public Exception? LastError => null;
#pragma warning disable CS0067
        public event EventHandler? Changed;
        public event EventHandler<LibraryErrorEventArgs>? Error;
#pragma warning restore CS0067
        public void Refresh() { }
        public void SetRootPath(string rootPath) { }
        public void Dispose() { }
    }
}
