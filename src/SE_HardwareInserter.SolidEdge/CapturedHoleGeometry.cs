using System;
using SolidEdgeAssembly;
using SolidEdgeFramework;

namespace SE_HardwareInserter.SolidEdge
{
    /// <summary>
    /// Referencias top-level del agujero capturado en un ensamblaje. Conserva únicamente
    /// <see cref="Reference"/> válidas para relaciones 3D y datos administrados independientes.
    /// </summary>
    public sealed class CapturedHoleGeometry : IDisposable
    {
        private Reference? _axisReference;
        private Reference? _supportReference;
        private AssemblyDocument? _assemblyDocument;
        private Array? _supportPoint;
        private bool _disposed;

        internal CapturedHoleGeometry(
            Reference axisReference,
            Reference? supportReference,
            Array? supportPoint,
            AssemblyDocument assemblyDocument,
            string assemblyFullName,
            double diameterMm)
        {
            if (axisReference == null) throw new ArgumentNullException(nameof(axisReference));
            if (assemblyDocument == null) throw new ArgumentNullException(nameof(assemblyDocument));
            if (string.IsNullOrWhiteSpace(assemblyFullName))
            {
                throw new ArgumentException("La identidad del ensamblaje no puede estar vacía.", nameof(assemblyFullName));
            }
            if (double.IsNaN(diameterMm) || double.IsInfinity(diameterMm) || diameterMm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(diameterMm));
            }
            if ((supportReference == null) != (supportPoint == null))
            {
                throw new ArgumentException("La referencia de apoyo y su punto deben existir conjuntamente.");
            }

            _axisReference = axisReference;
            _supportReference = supportReference;
            _supportPoint = supportPoint == null ? null : (Array)supportPoint.Clone();
            _assemblyDocument = assemblyDocument;
            AssemblyFullName = assemblyFullName;
            DiameterMm = diameterMm;
        }

        /// <summary>Referencia cilíndrica top-level usada por <c>AddAxial</c>.</summary>
        public Reference AxisReference => _axisReference
            ?? throw new ObjectDisposedException(nameof(CapturedHoleGeometry));

        /// <summary>Referencia plana top-level usada por <c>AddPlanar</c>, si fue seleccionada.</summary>
        public Reference? SupportReference
        {
            get
            {
                ThrowIfDisposed();
                return _supportReference;
            }
        }

        /// <summary>Punto XYZ sobre la cara de apoyo, o null si no se seleccionó esa cara.</summary>
        public Array? SupportPoint
        {
            get
            {
                ThrowIfDisposed();
                return _supportPoint == null ? null : (Array)_supportPoint.Clone();
            }
        }

        /// <summary>Ruta completa que identifica el ensamblaje donde se realizó la captura.</summary>
        public string AssemblyFullName { get; }

        /// <summary>Identidad COM viva del documento que originó la captura.</summary>
        internal AssemblyDocument AssemblyDocumentIdentity => _assemblyDocument
            ?? throw new ObjectDisposedException(nameof(CapturedHoleGeometry));

        /// <summary>Diámetro del agujero en milímetros.</summary>
        public double DiameterMm { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _supportPoint = null;
            ComInterop.ReleaseOwned(ref _supportReference);
            ComInterop.ReleaseOwned(ref _axisReference);
            ComInterop.ReleaseOwned(ref _assemblyDocument);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CapturedHoleGeometry));
            }
        }
    }
}
