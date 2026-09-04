using System;
using SolidEdgeAssembly;
using SolidEdgeGeometry;

namespace SE_HardwareInserter.SolidEdge
{
    /// <summary>Resultado de intentar resolver automáticamente las caras del hardware insertado.</summary>
    public enum AutomaticResolution
    {
        Unsupported,
        Ambiguous,
        Resolved,
    }

    /// <summary>Resultado propietario de una resolución automática de geometría.</summary>
    public sealed class ScrewGeometryResolution : IDisposable
    {
        private Face? _axisFace;
        private Face? _headBottomFace;
        private bool _disposed;

        private ScrewGeometryResolution(
            AutomaticResolution status,
            Face? axisFace,
            Face? headBottomFace,
            string message)
        {
            Status = status;
            _axisFace = axisFace;
            _headBottomFace = headBottomFace;
            Message = message ?? string.Empty;
        }

        /// <summary>Estado de la resolución.</summary>
        public AutomaticResolution Status { get; }

        /// <summary>Explicación adecuada para diagnóstico o presentación al usuario.</summary>
        public string Message { get; }

        /// <summary>Cara cilíndrica del vástago, usada como eje en <c>Relations3d.AddAxial</c>.</summary>
        public Face AxisFace => Status == AutomaticResolution.Resolved && _axisFace != null
            ? _axisFace
            : throw new InvalidOperationException("La resolución automática no contiene una cara axial.");

        /// <summary>Cara plana inferior de la cabeza, usada en <c>Relations3d.AddPlanar</c>.</summary>
        public Face HeadBottomFace => Status == AutomaticResolution.Resolved && _headBottomFace != null
            ? _headBottomFace
            : throw new InvalidOperationException("La resolución automática no contiene una cara de apoyo.");

        internal static ScrewGeometryResolution CreateResolved(Face axisFace, Face headBottomFace) =>
            new ScrewGeometryResolution(
                AutomaticResolution.Resolved,
                axisFace ?? throw new ArgumentNullException(nameof(axisFace)),
                headBottomFace ?? throw new ArgumentNullException(nameof(headBottomFace)),
                "Las caras axial y de apoyo tienen candidatos dominantes únicos.");

        internal static ScrewGeometryResolution CreateAmbiguous(string message) =>
            new ScrewGeometryResolution(AutomaticResolution.Ambiguous, null, null, message);

        internal static ScrewGeometryResolution CreateUnsupported(string message) =>
            new ScrewGeometryResolution(AutomaticResolution.Unsupported, null, null, message);

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ComInterop.ReleaseOwned(ref _headBottomFace);
            ComInterop.ReleaseOwned(ref _axisFace);
        }
    }

    /// <summary>
    /// Identifica, dentro del cuerpo de un tornillo recién insertado, cuál cara es el eje del
    /// vástago y cuál la cara de apoyo de la cabeza. No existe una API genérica de Solid Edge para
    /// esto: depende de la convención con la que se modelaron los archivos .par de tornillería.
    /// Punto de extensión: si las plantillas del servidor adoptan nombres de cara consistentes,
    /// añadir una implementación alternativa (ej. NamedFaceScrewGeometryResolver) sin tocar el resto del flujo.
    /// </summary>
    public interface IScrewGeometryResolver
    {
        ScrewGeometryResolution Resolve(Occurrence screwOccurrence, double holeDiameterMm);
    }
}
