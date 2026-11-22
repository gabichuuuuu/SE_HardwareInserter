using System;
using System.Collections.Generic;
using SE_HardwareInserter.Core.Diagnostics;
using SolidEdgeAssembly;
using SolidEdgeFramework;
using SolidEdgeGeometry;

namespace SE_HardwareInserter.SolidEdge
{
    /// <summary>Resultado observable de intentar revertir una inserción provisional.</summary>
    public sealed class RollbackResult
    {
        internal RollbackResult(IReadOnlyList<Exception> diagnostics, bool occurrenceDeleted)
        {
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            OccurrenceDeleted = occurrenceDeleted;
        }

        /// <summary>True cuando la occurrence provisional ya no permanece en el ensamblaje.</summary>
        public bool OccurrenceDeleted { get; }

        /// <summary>True cuando el estado final quedó limpio, aunque hubiera advertencias intermedias.</summary>
        public bool CleanupComplete => OccurrenceDeleted;

        /// <summary>Alias de compatibilidad de <see cref="CleanupComplete"/>.</summary>
        public bool Succeeded => CleanupComplete;

        /// <summary>Fallos históricos de borrado observados durante la limpieza.</summary>
        public IReadOnlyList<Exception> Diagnostics { get; }

        /// <summary>Alias de compatibilidad de <see cref="Diagnostics"/>.</summary>
        public IReadOnlyList<Exception> Failures => Diagnostics;
    }

    /// <summary>Indica que una reversión solicitada explícitamente quedó incompleta.</summary>
    public sealed class InsertionRollbackException : Exception
    {
        internal InsertionRollbackException(RollbackResult result)
            : base($"La occurrence provisional no pudo eliminarse ({result.Diagnostics.Count} diagnóstico(s) de limpieza).")
        {
            Result = result;
        }

        public RollbackResult Result { get; }
    }

    /// <summary>Conserva el fallo operativo original cuando además falla parte del rollback.</summary>
    public sealed class InsertionOperationException : Exception
    {
        internal InsertionOperationException(Exception operationFailure, RollbackResult rollback)
            : base(
                $"La operación de inserción falló y la limpieza quedó incompleta ({rollback.Failures.Count} fallo(s)).",
                operationFailure)
        {
            Rollback = rollback;
        }

        public RollbackResult Rollback { get; }
    }

    /// <summary>
    /// Inserción provisional de una occurrence. Si no se confirma mediante restricciones o
    /// <see cref="CommitFree"/>, <see cref="Dispose"/> elimina relaciones y occurrence.
    /// </summary>
    public sealed class InsertionSession : IDisposable
    {
        private readonly IScrewGeometryResolver _automaticResolver;
        private readonly IAppLogger _logger;
        private readonly string _operationId;
        private readonly string _assemblyFullName;
        private AssemblyDocument? _assembly;
        private Occurrences? _occurrences;
        private Occurrence? _occurrence;
        private Relations3d? _relations;
        private GroundRelation3d? _ground;
        private Reference? _screwAxisReference;
        private Reference? _screwSupportReference;
        private AxialRelation3d? _axial;
        private PlanarRelation3d? _planar;
        private bool _axialDeleted;
        private bool _planarDeleted;
        private bool _occurrenceDeleted;
        private bool _committed;
        private bool _failed;
        private bool _disposed;
        private readonly List<Exception> _rollbackFailures = new List<Exception>();

        internal InsertionSession(
            AssemblyDocument assembly,
            Occurrences occurrences,
            Occurrence occurrence,
            Relations3d relations,
            string assemblyFullName,
            IScrewGeometryResolver automaticResolver,
            IAppLogger logger,
            string operationId)
        {
            _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
            _occurrences = occurrences ?? throw new ArgumentNullException(nameof(occurrences));
            _occurrence = occurrence ?? throw new ArgumentNullException(nameof(occurrence));
            _relations = relations ?? throw new ArgumentNullException(nameof(relations));
            _assemblyFullName = !string.IsNullOrWhiteSpace(assemblyFullName)
                ? assemblyFullName
                : throw new ArgumentException("La identidad del ensamblaje no puede estar vacía.", nameof(assemblyFullName));
            _automaticResolver = automaticResolver ?? throw new ArgumentNullException(nameof(automaticResolver));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _operationId = !string.IsNullOrWhiteSpace(operationId)
                ? operationId
                : throw new ArgumentException("El identificador de operación no puede estar vacío.", nameof(operationId));
            LogInfo("begin", "Occurrence provisional creada y mantenida grounded.");
        }

        /// <summary>Identificador estable usado por todas las fases diagnósticas de la inserción.</summary>
        public string OperationId => _operationId;

        /// <summary>Identidad del ensamblaje propietario de la occurrence provisional.</summary>
        public string AssemblyFullName => _assemblyFullName;

        /// <summary>Diagnóstico de la última resolución automática no resuelta.</summary>
        public string LastAutomaticResolutionMessage { get; private set; } = string.Empty;

        /// <summary>Fallos de limpieza observados, incluidos los registrados por <see cref="Dispose"/>.</summary>
        public IReadOnlyList<Exception> LastRollbackFailures => _rollbackFailures.ToArray();

        /// <summary>Diagnósticos históricos producidos durante uno o más intentos de rollback.</summary>
        public IReadOnlyList<Exception> LastRollbackDiagnostics => _rollbackFailures.ToArray();

        /// <summary>Indica si se encontró la relación Ground al confirmar la operación.</summary>
        public bool? GroundWasPresent { get; private set; }

        /// <summary>
        /// Intenta resolver y aplicar restricciones automáticamente. Los resultados ambiguos o no
        /// soportados no eliminan la relación Ground y permiten continuar con selección guiada.
        /// </summary>
        public AutomaticResolution TryApplyAutomaticConstraints(CapturedHoleGeometry hole)
        {
            EnsureReady();
            ValidateHole(hole);
            LogInfo("automatic", "Comienza la resolución automática de geometría.");

            using (var resolution = _automaticResolver.Resolve(Occurrence, hole.DiameterMm))
            {
                LastAutomaticResolutionMessage = resolution.Message;
                if (resolution.Status != AutomaticResolution.Resolved)
                {
                    LogInfo(
                        "automatic",
                        "La resolución automática requiere intervención del usuario.",
                        new Dictionary<string, string> { ["result"] = resolution.Status.ToString() });
                    return resolution.Status;
                }

                Reference? axisReference = null;
                Reference? supportReference = null;
                try
                {
                    axisReference = CreateOwnedReference(resolution.AxisFace);
                    Array? supportPoint = null;
                    if (hole.SupportReference != null)
                    {
                        supportReference = CreateOwnedReference(resolution.HeadBottomFace);
                        supportPoint = GeometryInterop.GetPointOnFace(resolution.HeadBottomFace);
                    }

                    ApplyOwnedConstraints(hole, ref axisReference, ref supportReference, supportPoint);
                    LogInfo("commit", "Inserción confirmada con restricciones automáticas.");
                    return AutomaticResolution.Resolved;
                }
                catch (Exception ex)
                {
                    var rollback = MarkFailedAndRollback();
                    LogRollbackResult("automatic", ex, rollback);
                    if (!rollback.Succeeded) throw new InsertionOperationException(ex, rollback);
                    throw;
                }
                finally
                {
                    ComInterop.ReleaseOwned(ref supportReference);
                    ComInterop.ReleaseOwned(ref axisReference);
                }
            }
        }

        /// <summary>
        /// Valida sin modificar la inserción que las referencias guiadas tienen la geometría
        /// requerida y pertenecen a la occurrence provisional. La aplicación posterior vuelve
        /// a validarlas para evitar usar una selección que haya cambiado.
        /// </summary>
        public void ValidateGuidedReferences(
            Reference screwAxisReference,
            Reference screwSupportReference)
        {
            EnsureReady();
            if (screwAxisReference == null) throw new ArgumentNullException(nameof(screwAxisReference));
            if (screwSupportReference == null) throw new ArgumentNullException(nameof(screwSupportReference));

            ValidateGuidedReference(screwAxisReference, expectCylinder: true);
            ValidateGuidedReference(screwSupportReference, expectCylinder: false);
        }

        /// <summary>
        /// Aplica restricciones a partir de referencias seleccionadas sobre la occurrence provisional.
        /// Las referencias recibidas son prestadas; la sesión crea y posee sus propias referencias.
        /// </summary>
        public void ApplyGuidedConstraints(
            CapturedHoleGeometry hole,
            Reference screwAxisReference,
            Reference? screwSupportReference)
        {
            EnsureReady();
            ValidateHole(hole);
            LogInfo("guided", "Comienza la aplicación de referencias seleccionadas.");
            if (screwAxisReference == null) throw new ArgumentNullException(nameof(screwAxisReference));
            if (hole.SupportReference != null && screwSupportReference == null)
            {
                throw new ArgumentNullException(
                    nameof(screwSupportReference),
                    "La captura contiene apoyo plano y requiere una referencia plana del hardware.");
            }

            Reference? ownedAxis = null;
            Reference? ownedSupport = null;
            try
            {
                ownedAxis = CloneGuidedReference(screwAxisReference, expectCylinder: true, out _);
                Array? supportPoint = null;
                if (hole.SupportReference != null && screwSupportReference != null)
                {
                    ownedSupport = CloneGuidedReference(
                        screwSupportReference,
                        expectCylinder: false,
                        out supportPoint);
                }

                ApplyOwnedConstraints(hole, ref ownedAxis, ref ownedSupport, supportPoint);
                LogInfo("commit", "Inserción confirmada con selección guiada.");
            }
            catch (Exception ex)
            {
                var rollback = MarkFailedAndRollback();
                LogRollbackResult("guided", ex, rollback);
                if (!rollback.Succeeded) throw new InsertionOperationException(ex, rollback);
                throw;
            }
            finally
            {
                ComInterop.ReleaseOwned(ref ownedSupport);
                ComInterop.ReleaseOwned(ref ownedAxis);
            }
        }

        /// <summary>Confirma una inserción libre eliminando su relación Ground inicial.</summary>
        public void CommitFree()
        {
            EnsureReady();
            LogInfo("free", "Comienza la confirmación como inserción libre.");
            try
            {
                TryDeleteGroundRelation();
                _committed = true;
                LogInfo("commit", "Inserción libre confirmada.");
            }
            catch (Exception ex)
            {
                var rollback = MarkFailedAndRollback();
                LogRollbackResult("free", ex, rollback);
                if (!rollback.Succeeded) throw new InsertionOperationException(ex, rollback);
                throw;
            }
        }

        /// <summary>Revierte explícitamente la inserción o informa mediante excepción si queda incompleta.</summary>
        public RollbackResult Rollback()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(InsertionSession));
            if (_committed) throw new InvalidOperationException("Una inserción confirmada no puede revertirse desde esta sesión.");
            LogInfo("rollback", "Comienza el rollback explícito.");
            _failed = true;
            var result = RollbackInternal();
            if (!result.Succeeded)
            {
                LogError("rollback-incomplete", "El rollback explícito quedó incompleto.");
                throw new InsertionRollbackException(result);
            }
            LogInfo("rollback", "Rollback completado.");
            return result;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!_committed)
            {
                var rollback = RollbackInternal();
                if (rollback.Succeeded) LogInfo("rollback", "Rollback completado durante Dispose.");
                else LogError("rollback-incomplete", "El rollback durante Dispose quedó incompleto.");
            }
            ReleaseOwnedObjects();
        }

        private AssemblyDocument Assembly => _assembly
            ?? throw new ObjectDisposedException(nameof(InsertionSession));

        private Occurrence Occurrence => _occurrence
            ?? throw new ObjectDisposedException(nameof(InsertionSession));

        private Relations3d Relations => _relations
            ?? throw new ObjectDisposedException(nameof(InsertionSession));

        private void EnsureReady()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(InsertionSession));
            if (_committed) throw new InvalidOperationException("La inserción ya fue confirmada.");
            if (_failed) throw new InvalidOperationException("La inserción falló y ya se intentó revertir.");
        }

        private void ValidateHole(CapturedHoleGeometry hole)
        {
            if (hole == null) throw new ArgumentNullException(nameof(hole));
            if (!ComInterop.HasSameIdentity(hole.AssemblyDocumentIdentity, Assembly))
            {
                throw new InvalidOperationException(
                    "La geometría capturada pertenece a otro ensamblaje. Vuelva a capturar el agujero.");
            }
            if (!string.Equals(hole.AssemblyFullName, _assemblyFullName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "La identidad de ruta del ensamblaje cambió desde la captura. Vuelva a capturar el agujero.");
            }
        }

        private Reference CloneGuidedReference(
            Reference borrowedReference,
            bool expectCylinder,
            out Array? point)
        {
            ValidateReferenceBelongsToOccurrence(borrowedReference);
            Face? face = null;
            try
            {
                face = GeometryInterop.GetOwnedFace(borrowedReference);
                if (expectCylinder)
                {
                    GeometryInterop.EnsureCylinder(face);
                    point = null;
                }
                else
                {
                    GeometryInterop.EnsurePlane(face);
                    point = GeometryInterop.GetPointOnFace(face);
                }
                return CreateOwnedReference(face);
            }
            finally
            {
                ComInterop.ReleaseOwned(ref face);
            }
        }

        private void ValidateGuidedReference(Reference reference, bool expectCylinder)
        {
            ValidateReferenceBelongsToOccurrence(reference);
            Face? face = null;
            try
            {
                face = GeometryInterop.GetOwnedFace(reference);
                if (expectCylinder)
                {
                    GeometryInterop.EnsureCylinder(face);
                }
                else
                {
                    GeometryInterop.EnsurePlane(face);
                }
            }
            finally
            {
                ComInterop.ReleaseOwned(ref face);
            }
        }

        private void ValidateReferenceBelongsToOccurrence(Reference reference)
        {
            object? topOccurrence = null;
            try
            {
                topOccurrence = ComInterop.GetOwnedTopOccurrence(reference);
                if (!ComInterop.HasSameIdentity(topOccurrence, Occurrence))
                {
                    throw new InvalidOperationException(
                        "La referencia guiada no pertenece a la occurrence provisional insertada.");
                }
            }
            finally
            {
                ComInterop.ReleaseOwned(ref topOccurrence);
            }
        }

        private Reference CreateOwnedReference(Face face)
        {
            object? created = null;
            try
            {
                created = Assembly.CreateReference(Occurrence, face);
                if (!(created is Reference reference))
                {
                    throw new InvalidOperationException("Solid Edge no devolvió una Reference de ensamblaje válida.");
                }
                created = null;
                return reference;
            }
            finally
            {
                ComInterop.ReleaseOwned(ref created);
            }
        }

        private void ApplyOwnedConstraints(
            CapturedHoleGeometry hole,
            ref Reference? axisReference,
            ref Reference? supportReference,
            Array? screwSupportPoint)
        {
            if (axisReference == null) throw new ArgumentNullException(nameof(axisReference));
            _screwAxisReference = axisReference;
            axisReference = null;
            _screwSupportReference = supportReference;
            supportReference = null;

            TryDeleteGroundRelation();

            _axial = Relations.AddAxial(
                _screwAxisReference,
                hole.AxisReference,
                NormalsAligned: false);
            if (!IsSolved(_axial))
            {
                _axial.Flip();
            }
            if (!IsSolved(_axial))
            {
                throw CreateUnsolvedException("axial", _axial.Status, _axial.DetailedStatus);
            }

            var holeSupportReference = hole.SupportReference;
            if (holeSupportReference != null)
            {
                if (_screwSupportReference == null || screwSupportPoint == null)
                {
                    throw new InvalidOperationException("Faltan la referencia o el punto de apoyo del hardware.");
                }

                var holeSupportPoint = hole.SupportPoint
                    ?? throw new InvalidOperationException("La captura no conserva el punto de apoyo.");
                AddPlanarWithOrientationRetry(
                    _screwSupportReference,
                    holeSupportReference,
                    screwSupportPoint,
                    holeSupportPoint);
            }

            if (!IsSolved(_axial))
            {
                throw CreateUnsolvedException("axial", _axial.Status, _axial.DetailedStatus);
            }
            _axial.FixedRotate = true;
            _committed = true;
        }

        private void AddPlanarWithOrientationRetry(
            Reference screwSupport,
            Reference holeSupport,
            Array screwPoint,
            Array holePoint)
        {
            var firstScrewPoint = (Array)screwPoint.Clone();
            var firstHolePoint = (Array)holePoint.Clone();
            _planar = Relations.AddPlanar(
                screwSupport,
                holeSupport,
                NormalsAligned: true,
                ref firstScrewPoint,
                ref firstHolePoint);
            if (IsSolved(_planar)) return;

            _planar.Delete();
            _planarDeleted = true;
            ComInterop.ReleaseOwned(ref _planar);

            var retryScrewPoint = (Array)screwPoint.Clone();
            var retryHolePoint = (Array)holePoint.Clone();
            _planar = Relations.AddPlanar(
                screwSupport,
                holeSupport,
                NormalsAligned: false,
                ref retryScrewPoint,
                ref retryHolePoint);
            _planarDeleted = false;
            if (!IsSolved(_planar))
            {
                throw CreateUnsolvedException("planar", _planar.Status, _planar.DetailedStatus);
            }
        }

        private bool TryDeleteGroundRelation()
        {
            object? occurrenceRelationsObject = null;
            Relations3d? occurrenceRelations = null;
            object? relationObject = null;
            try
            {
                occurrenceRelationsObject = Occurrence.Relations3d;
                occurrenceRelations = occurrenceRelationsObject as Relations3d
                    ?? throw new InvalidOperationException("Solid Edge no devolvió las relaciones de la occurrence.");
                occurrenceRelationsObject = null;

                for (var index = 1; index <= occurrenceRelations.Count; index++)
                {
                    relationObject = occurrenceRelations.Item(index);
                    if (relationObject is GroundRelation3d ground)
                    {
                        _ground = ground;
                        relationObject = null;
                        break;
                    }
                    ComInterop.ReleaseOwned(ref relationObject);
                }

                if (_ground == null)
                {
                    GroundWasPresent = false;
                    return false;
                }
                GroundWasPresent = true;
                _ground.Delete();
                return true;
            }
            finally
            {
                ComInterop.ReleaseOwned(ref relationObject);
                ComInterop.ReleaseOwned(ref occurrenceRelations);
                ComInterop.ReleaseOwned(ref occurrenceRelationsObject);
            }
        }

        private static bool IsSolved(AxialRelation3d relation) =>
            relation.Status == Relation3dStatusConstants.igRelation3dStatusSolved &&
            relation.DetailedStatus == Relation3dDetailedStatusConstants.igRelation3dDetailedStatusSolved;

        private static bool IsSolved(PlanarRelation3d relation) =>
            relation.Status == Relation3dStatusConstants.igRelation3dStatusSolved &&
            relation.DetailedStatus == Relation3dDetailedStatusConstants.igRelation3dDetailedStatusSolved;

        private static InvalidOperationException CreateUnsolvedException(
            string relationName,
            Relation3dStatusConstants status,
            Relation3dDetailedStatusConstants detailedStatus) =>
            new InvalidOperationException(
                $"La relación {relationName} no quedó resuelta (Status={status}, DetailedStatus={detailedStatus}).");

        private RollbackResult MarkFailedAndRollback()
        {
            _failed = true;
            return RollbackInternal();
        }

        private RollbackResult RollbackInternal()
        {
            if (_occurrenceDeleted)
            {
                return new RollbackResult(Array.Empty<Exception>(), occurrenceDeleted: true);
            }

            var failures = new List<Exception>();
            if (!_planarDeleted && _planar != null)
            {
                try
                {
                    _planar.Delete();
                    _planarDeleted = true;
                }
                catch (Exception ex)
                {
                    failures.Add(new InvalidOperationException("No se pudo eliminar la relación planar.", ex));
                }
            }
            if (!_axialDeleted && _axial != null)
            {
                try
                {
                    _axial.Delete();
                    _axialDeleted = true;
                }
                catch (Exception ex)
                {
                    failures.Add(new InvalidOperationException("No se pudo eliminar la relación axial.", ex));
                }
            }
            if (!_occurrenceDeleted && _occurrence != null)
            {
                try
                {
                    _occurrence.Delete();
                    _occurrenceDeleted = true;
                }
                catch (Exception ex)
                {
                    failures.Add(new InvalidOperationException("No se pudo eliminar la occurrence provisional.", ex));
                }
            }
            _rollbackFailures.AddRange(failures);
            return new RollbackResult(failures.ToArray(), _occurrenceDeleted);
        }

        private void LogRollbackResult(string phase, Exception exception, RollbackResult rollback)
        {
            if (rollback.Succeeded)
            {
                LogError(phase, "La operación falló y el rollback se completó.", exception);
            }
            else
            {
                LogError("rollback-incomplete", "La operación falló y el rollback quedó incompleto.", exception);
            }
        }

        private void LogInfo(
            string phase,
            string message,
            IReadOnlyDictionary<string, string>? context = null)
        {
            try
            {
                _logger.Info(_operationId, phase, message, context: context);
            }
            catch (Exception)
            {
                // Un logger inyectado no puede alterar la transacción COM.
            }
        }

        private void LogError(
            string phase,
            string message,
            Exception? exception = null,
            IReadOnlyDictionary<string, string>? context = null)
        {
            try
            {
                _logger.Error(_operationId, phase, message, exception, context);
            }
            catch (Exception)
            {
                // Un logger inyectado no puede alterar la transacción COM.
            }
        }

        private void ReleaseOwnedObjects()
        {
            ComInterop.ReleaseOwned(ref _planar);
            ComInterop.ReleaseOwned(ref _axial);
            ComInterop.ReleaseOwned(ref _ground);
            ComInterop.ReleaseOwned(ref _screwSupportReference);
            ComInterop.ReleaseOwned(ref _screwAxisReference);
            ComInterop.ReleaseOwned(ref _relations);
            ComInterop.ReleaseOwned(ref _occurrence);
            ComInterop.ReleaseOwned(ref _occurrences);
            ComInterop.ReleaseOwned(ref _assembly);
        }
    }
}
