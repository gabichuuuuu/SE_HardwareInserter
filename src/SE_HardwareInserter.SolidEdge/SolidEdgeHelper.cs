using System;
using SE_HardwareInserter.Core.Diagnostics;
using SolidEdgeAssembly;
using SolidEdgeFramework;
using SolidEdgeGeometry;

namespace SE_HardwareInserter.SolidEdge
{
    /// <summary>
    /// Envuelve todas las llamadas a la API de Solid Edge: captura de geometría del agujero,
    /// inserción del hardware (.par) como occurrence y, opcionalmente, restricción axial/planar
    /// contra el agujero. Los RCW adquiridos por esta capa tienen propiedad explícita; la instancia
    /// <see cref="Application"/> recibida del host siempre es prestada y nunca se libera aquí.
    /// </summary>
    /// <remarks>
    /// Esta clase recibe <see cref="Application"/> por parámetro; el add-in COM obtiene la instancia
    /// en <c>OnConnection</c> y se la inyecta. La clase no inicia ni localiza sesiones de Solid Edge.
    /// </remarks>
    public sealed class SolidEdgeHelper
    {
        private readonly IScrewGeometryResolver _screwGeometryResolver;
        private readonly IAppLogger _logger;

        public SolidEdgeHelper(IScrewGeometryResolver screwGeometryResolver)
            : this(screwGeometryResolver, NullAppLogger.Instance)
        {
        }

        public SolidEdgeHelper(IScrewGeometryResolver screwGeometryResolver, IAppLogger logger)
        {
            _screwGeometryResolver = screwGeometryResolver
                ?? throw new ArgumentNullException(nameof(screwGeometryResolver));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Captura referencias top-level del <c>SelectSet</c> activo. Solid Edge debe devolver
        /// <see cref="Reference"/> cuyos objetos sean caras; una <see cref="Face"/> sin referencia
        /// de ensamblaje se rechaza porque no puede persistirse con seguridad para una relación 3D.
        /// </summary>
        public CapturedHoleGeometry CaptureSelectedHoleGeometry(Application application)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));

            AssemblyDocument? assembly = null;
            SelectSet? selectSet = null;
            Reference? axisReference = null;
            Reference? supportReference = null;
            Array? supportPoint = null;
            var transferred = false;
            try
            {
                assembly = GetActiveAssemblyOwned(application);
                var identity = ReadIdentity(assembly);
                selectSet = application.ActiveSelectSet;
                if (selectSet.Count == 0)
                {
                    throw new InvalidOperationException(
                        "Seleccione la cara cilíndrica interior del agujero (y opcionalmente su cara de apoyo) antes de capturar.");
                }

                for (var i = 1; i <= selectSet.Count; i++)
                {
                    object? item = selectSet.Item(i);
                    if (item is Face)
                    {
                        ComInterop.ReleaseOwned(ref item);
                        throw new InvalidOperationException(
                            "Solid Edge devolvió una Face sin Reference top-level. Seleccione la cara desde el ensamblaje, no desde una pieza activada.");
                    }
                    Reference? reference = item as Reference;
                    if (reference == null)
                    {
                        ComInterop.ReleaseOwned(ref item);
                        continue;
                    }
                    item = null;

                    Face? face = null;
                    try
                    {
                        face = GeometryInterop.GetOwnedFace(reference);
                        if (axisReference == null && IsGeometry<Cylinder>(face))
                        {
                            axisReference = reference;
                            reference = null;
                        }
                        else if (supportReference == null && IsGeometry<Plane>(face))
                        {
                            supportPoint = GeometryInterop.GetPointOnFace(face);
                            supportReference = reference;
                            reference = null;
                        }
                    }
                    finally
                    {
                        ComInterop.ReleaseOwned(ref face);
                        ComInterop.ReleaseOwned(ref reference);
                    }
                }

                if (axisReference == null)
                {
                    throw new InvalidOperationException(
                        "Ninguna referencia seleccionada apunta a una cara cilíndrica.");
                }
                ValidateCapturedReferencesShareOccurrence(axisReference, supportReference);

                Face? axisFace = null;
                try
                {
                    axisFace = GeometryInterop.GetOwnedFace(axisReference);
                    var diameterMm = GeometryInterop.GetCylinderDiameterMm(axisFace);
                    var result = new CapturedHoleGeometry(
                        axisReference,
                        supportReference,
                        supportPoint,
                        assembly,
                        identity.FullName,
                        diameterMm);
                    axisReference = null;
                    supportReference = null;
                    assembly = null;
                    transferred = true;
                    return result;
                }
                finally
                {
                    ComInterop.ReleaseOwned(ref axisFace);
                }
            }
            finally
            {
                if (!transferred)
                {
                    ComInterop.ReleaseOwned(ref supportReference);
                    ComInterop.ReleaseOwned(ref axisReference);
                }
                ComInterop.ReleaseOwned(ref selectSet);
                ComInterop.ReleaseOwned(ref assembly);
            }
        }

        /// <summary>Inicia una inserción provisional, inicialmente grounded y todavía no confirmada.</summary>
        public InsertionSession BeginInsertion(Application application, string localParPath)
        {
            return BeginInsertion(application, localParPath, Guid.NewGuid().ToString("N"));
        }

        /// <summary>Inicia una inserción provisional asociada al identificador de diagnóstico indicado.</summary>
        public InsertionSession BeginInsertion(
            Application application,
            string localParPath,
            string operationId)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));
            if (string.IsNullOrWhiteSpace(localParPath))
            {
                throw new ArgumentException("La ruta del archivo .par no puede estar vacía.", nameof(localParPath));
            }
            if (string.IsNullOrWhiteSpace(operationId))
            {
                throw new ArgumentException("El identificador de operación no puede estar vacío.", nameof(operationId));
            }

            AssemblyDocument? assembly = null;
            Occurrences? occurrences = null;
            Occurrence? occurrence = null;
            Relations3d? relations = null;
            var transferred = false;
            try
            {
                assembly = GetActiveAssemblyOwned(application);
                var identity = ReadIdentity(assembly);
                occurrences = assembly.Occurrences;
                occurrence = occurrences.AddByFilename(localParPath, Type.Missing);
                relations = assembly.Relations3d;

                var session = new InsertionSession(
                    assembly,
                    occurrences,
                    occurrence,
                    relations,
                    identity.FullName,
                    _screwGeometryResolver,
                    _logger,
                    operationId);
                assembly = null;
                occurrences = null;
                occurrence = null;
                relations = null;
                transferred = true;
                return session;
            }
            catch (Exception ex)
            {
                if (occurrence != null)
                {
                    var cleanupDiagnostics = new System.Collections.Generic.List<Exception>();
                    var occurrenceDeleted = false;
                    try
                    {
                        occurrence.Delete();
                        occurrenceDeleted = true;
                    }
                    catch (Exception cleanupException)
                    {
                        cleanupDiagnostics.Add(new InvalidOperationException(
                            "No se pudo eliminar la occurrence creada antes de fallar BeginInsertion.",
                            cleanupException));
                    }

                    var rollback = new RollbackResult(cleanupDiagnostics.ToArray(), occurrenceDeleted);
                    if (!rollback.CleanupComplete)
                    {
                        throw new InsertionOperationException(ex, rollback);
                    }
                }
                throw;
            }
            finally
            {
                if (!transferred)
                {
                    ComInterop.ReleaseOwned(ref relations);
                    ComInterop.ReleaseOwned(ref occurrence);
                    ComInterop.ReleaseOwned(ref occurrences);
                    ComInterop.ReleaseOwned(ref assembly);
                }
            }
        }

        /// <summary>
        /// API compatible para el flujo actual de la UI. Las inserciones automáticas ambiguas se
        /// revierten y solicitan selección guiada en vez de aplicar una heurística insegura.
        /// </summary>
        public void InsertOccurrenceAndConstrain(
            Application application,
            string localParPath,
            CapturedHoleGeometry? hole,
            bool applyConstraints)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));
            if (applyConstraints && hole == null) throw new ArgumentNullException(nameof(hole));

            using (var session = BeginInsertion(application, localParPath))
            {
                if (!applyConstraints)
                {
                    session.CommitFree();
                    return;
                }
                var resolution = session.TryApplyAutomaticConstraints(hole!);
                if (resolution != AutomaticResolution.Resolved)
                {
                    session.Rollback();
                    throw new InvalidOperationException(
                        session.LastAutomaticResolutionMessage + " La occurrence provisional fue eliminada; use selección guiada.");
                }
            }
        }

        /// <summary>Lee directorio e identidad del ensamblaje activo guardado.</summary>
        public static AssemblyIdentity GetActiveAssemblyIdentity(Application application)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));
            AssemblyDocument? assembly = null;
            try
            {
                assembly = GetActiveAssemblyOwned(application);
                return ReadIdentity(assembly);
            }
            finally
            {
                ComInterop.ReleaseOwned(ref assembly);
            }
        }

        /// <summary>Directorio del ensamblaje activo, o null si todavía no ha sido guardado.</summary>
        public static string? GetActiveAssemblyDirectory(Application application)
        {
            try
            {
                return GetActiveAssemblyIdentity(application).Directory;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>Alias compatible; devuelve el directorio, no una ruta de archivo.</summary>
        public static string? GetActiveAssemblyPath(Application application)
        {
            return GetActiveAssemblyDirectory(application);
        }

        private static AssemblyDocument GetActiveAssemblyOwned(Application application)
        {
            var documentType = application.ActiveDocumentType;
            if (documentType != SolidEdgeFramework.DocumentTypeConstants.igAssemblyDocument &&
                documentType != SolidEdgeFramework.DocumentTypeConstants.igSyncAssemblyDocument &&
                documentType != SolidEdgeFramework.DocumentTypeConstants.igWeldmentAssemblyDocument)
            {
                throw new InvalidOperationException("El documento activo no es un ensamblaje compatible.");
            }

            object? activeDocument = null;
            try
            {
                activeDocument = application.ActiveDocument;
                if (!(activeDocument is AssemblyDocument assembly))
                {
                    throw new InvalidOperationException("Solid Edge no devolvió un AssemblyDocument válido.");
                }
                activeDocument = null;
                return assembly;
            }
            finally
            {
                ComInterop.ReleaseOwned(ref activeDocument);
            }
        }

        private static AssemblyIdentity ReadIdentity(AssemblyDocument assembly)
        {
            var directory = assembly.Path;
            var fullName = assembly.FullName;
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fullName))
            {
                throw new InvalidOperationException("Guarde el ensamblaje antes de insertar hardware.");
            }
            return new AssemblyIdentity(directory, fullName);
        }

        private static bool IsGeometry<TGeometry>(Face face) where TGeometry : class
        {
            object? geometry = null;
            try
            {
                geometry = face.Geometry;
                return geometry is TGeometry;
            }
            finally
            {
                ComInterop.ReleaseOwned(ref geometry);
            }
        }

        private static void ValidateCapturedReferencesShareOccurrence(
            Reference axisReference,
            Reference? supportReference)
        {
            if (supportReference == null) return;

            object? axisOccurrence = null;
            object? supportOccurrence = null;
            try
            {
                axisOccurrence = ComInterop.GetOwnedTopOccurrence(axisReference);
                supportOccurrence = ComInterop.GetOwnedTopOccurrence(supportReference);
                if (!ComInterop.HasSameIdentity(axisOccurrence, supportOccurrence))
                {
                    throw new InvalidOperationException(
                        "Las referencias axial y de apoyo pertenecen a occurrences distintas.");
                }
            }
            finally
            {
                ComInterop.ReleaseOwned(ref supportOccurrence);
                ComInterop.ReleaseOwned(ref axisOccurrence);
            }
        }
    }
}
