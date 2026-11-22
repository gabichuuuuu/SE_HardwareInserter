using System;
using SolidEdgeFramework;
using SolidEdgeGeometry;

namespace SE_HardwareInserter.SolidEdge
{
    /// <summary>
    /// Selección guiada propietaria de las referencias axial y de apoyo de una occurrence.
    /// La primera selección debe ser cilíndrica y la segunda plana.
    /// </summary>
    public sealed class GuidedScrewSelection : IDisposable
    {
        private Reference? _axisReference;
        private Reference? _supportReference;
        private bool _disposed;

        private GuidedScrewSelection(Reference axisReference, Reference supportReference)
        {
            _axisReference = axisReference ?? throw new ArgumentNullException(nameof(axisReference));
            _supportReference = supportReference ?? throw new ArgumentNullException(nameof(supportReference));
        }

        /// <summary>Referencia cilíndrica seleccionada en primer lugar.</summary>
        public Reference AxisReference => _axisReference
            ?? throw new ObjectDisposedException(nameof(GuidedScrewSelection));

        /// <summary>Referencia plana seleccionada en segundo lugar.</summary>
        public Reference SupportReference => _supportReference
            ?? throw new ObjectDisposedException(nameof(GuidedScrewSelection));

        /// <summary>
        /// Captura las dos primeras referencias del SelectSet activo y comprueba su geometría y
        /// que ambas pertenecen a la misma occurrence. Las referencias devueltas son propiedad
        /// de esta instancia hasta <see cref="Dispose"/>.
        /// </summary>
        public static GuidedScrewSelection Capture(Application application, InsertionSession insertionSession)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));
            if (insertionSession == null) throw new ArgumentNullException(nameof(insertionSession));

            SelectSet? selectSet = null;
            Reference? axisReference = null;
            Reference? supportReference = null;
            var transferred = false;
            try
            {
                selectSet = application.ActiveSelectSet;
                if (selectSet.Count < 2)
                {
                    throw new InvalidOperationException(
                        "Seleccione primero la cara cilíndrica del vástago y después la cara plana de apoyo.");
                }

                axisReference = GetOwnedReference(selectSet, 1, "primera");
                supportReference = GetOwnedReference(selectSet, 2, "segunda");
                ValidateGeometry(axisReference, expectCylinder: true);
                ValidateGeometry(supportReference, expectCylinder: false);
                ValidateSameOccurrence(axisReference, supportReference);
                insertionSession.ValidateGuidedReferences(axisReference, supportReference);

                var result = new GuidedScrewSelection(axisReference, supportReference);
                axisReference = null;
                supportReference = null;
                transferred = true;
                return result;
            }
            finally
            {
                if (!transferred)
                {
                    ComInterop.ReleaseOwned(ref supportReference);
                    ComInterop.ReleaseOwned(ref axisReference);
                }
                ComInterop.ReleaseOwned(ref selectSet);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ComInterop.ReleaseOwned(ref _supportReference);
            ComInterop.ReleaseOwned(ref _axisReference);
        }

        private static Reference GetOwnedReference(SelectSet selectSet, int index, string position)
        {
            object? selected = null;
            try
            {
                selected = selectSet.Item(index);
                if (selected is Face)
                {
                    throw new InvalidOperationException(
                        $"La {position} selección es una Face sin Reference de ensamblaje. Seleccione desde el ensamblaje, no desde una pieza activada.");
                }
                if (!(selected is Reference reference))
                {
                    throw new InvalidOperationException(
                        $"La {position} selección no es una referencia de cara válida.");
                }

                selected = null;
                return reference;
            }
            finally
            {
                ComInterop.ReleaseOwned(ref selected);
            }
        }

        private static void ValidateGeometry(Reference reference, bool expectCylinder)
        {
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

        private static void ValidateSameOccurrence(Reference axisReference, Reference supportReference)
        {
            object? axisOccurrence = null;
            object? supportOccurrence = null;
            try
            {
                axisOccurrence = ComInterop.GetOwnedTopOccurrence(axisReference);
                supportOccurrence = ComInterop.GetOwnedTopOccurrence(supportReference);
                if (!ComInterop.HasSameIdentity(axisOccurrence, supportOccurrence))
                {
                    throw new InvalidOperationException(
                        "Las caras axial y de apoyo seleccionadas pertenecen a occurrences distintas.");
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
