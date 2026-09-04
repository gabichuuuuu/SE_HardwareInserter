using System;
using System.Collections.Generic;
using System.Linq;
using SE_HardwareInserter.Core.Geometry;
using SolidEdgeAssembly;
using SolidEdgeGeometry;

namespace SE_HardwareInserter.SolidEdge
{
    /// <summary>
    /// Adapta la topología COM a descriptores administrados y delega la decisión en
    /// <see cref="FastenerGeometryClassifier"/>. Una topología no soportada no se elige en silencio.
    /// </summary>
    /// <remarks>
    /// Los puntos base/root de las superficies analíticas se usan como heurística de proximidad;
    /// cada release debe validarla con el corpus real de piezas antes de habilitarlo en producción.
    /// </remarks>
    public sealed class IndexBasedScrewGeometryResolver : IScrewGeometryResolver
    {
        private const double MetresToMillimetres = 1000.0;
        private const double SquareMetresToSquareMillimetres = 1000000.0;
        private readonly FastenerGeometryClassifier _classifier = new FastenerGeometryClassifier();

        public ScrewGeometryResolution Resolve(Occurrence screwOccurrence, double holeDiameterMm)
        {
            if (screwOccurrence == null) throw new ArgumentNullException(nameof(screwOccurrence));
            if (double.IsNaN(holeDiameterMm) || double.IsInfinity(holeDiameterMm) || holeDiameterMm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(holeDiameterMm));
            }

            object? bodyObject = null;
            object? facesObject = null;
            var cylinderCandidates = new List<OwnedCylinderCandidate>();
            var planeCandidates = new List<OwnedPlaneCandidate>();
            var topologyIncomplete = false;
            try
            {
                bodyObject = screwOccurrence.Body;
                var body = bodyObject as Body;
                if (body == null)
                {
                    return ScrewGeometryResolution.CreateUnsupported(
                        "La occurrence no expone un Body compatible con la interop cargada.");
                }

                facesObject = body.Faces[FeatureTopologyQueryTypeConstants.igQueryAll];
                var faces = facesObject as Faces;
                if (faces == null)
                {
                    return ScrewGeometryResolution.CreateUnsupported(
                        "El Body no expone una colección Faces compatible con la interop cargada.");
                }

                for (var index = 1; index <= faces.Count; index++)
                {
                    object? faceObject = faces.Item(index);
                    var face = faceObject as Face;
                    if (face == null)
                    {
                        topologyIncomplete = true;
                        ComInterop.ReleaseOwned(ref faceObject);
                        continue;
                    }
                    faceObject = null;

                    object? geometry = null;
                    var retained = false;
                    try
                    {
                        geometry = face.Geometry;
                        if (geometry is Cylinder cylinder)
                        {
                            var descriptor = DescribeCylinder(index, face, cylinder);
                            if (descriptor != null)
                            {
                                cylinderCandidates.Add(new OwnedCylinderCandidate(face, descriptor));
                                retained = true;
                            }
                            else
                            {
                                topologyIncomplete = true;
                            }
                        }
                        else if (geometry is Plane plane)
                        {
                            var descriptor = DescribePlane(index, face, plane);
                            if (descriptor != null)
                            {
                                planeCandidates.Add(new OwnedPlaneCandidate(face, descriptor));
                                retained = true;
                            }
                            else
                            {
                                topologyIncomplete = true;
                            }
                        }
                    }
                    finally
                    {
                        ComInterop.ReleaseOwned(ref geometry);
                        if (!retained) ComInterop.ReleaseOwned(ref face);
                    }
                }

                var classification = _classifier.Classify(
                    holeDiameterMm,
                    cylinderCandidates.Select(candidate => candidate.Descriptor).ToList(),
                    planeCandidates.Select(candidate => candidate.Descriptor).ToList());
                if (classification.Status == FastenerGeometryStatus.Unsupported)
                {
                    return ScrewGeometryResolution.CreateUnsupported(
                        AppendTopologyWarning(classification.Message, topologyIncomplete));
                }
                if (classification.Status == FastenerGeometryStatus.Ambiguous)
                {
                    return ScrewGeometryResolution.CreateAmbiguous(
                        AppendTopologyWarning(classification.Message, topologyIncomplete));
                }
                if (topologyIncomplete)
                {
                    return ScrewGeometryResolution.CreateAmbiguous(
                        classification.Message +
                        " La topología estaba incompleta y no permite confirmar automáticamente la pareja.");
                }

                var selectedCylinder = cylinderCandidates.Single(
                    candidate => candidate.Descriptor.SourceIndex == classification.CylinderSourceIndex);
                var selectedPlane = planeCandidates.Single(
                    candidate => candidate.Descriptor.SourceIndex == classification.PlaneSourceIndex);
                Face? selectedAxisFace = null;
                Face? selectedSupportFace = null;
                try
                {
                    selectedAxisFace = selectedCylinder.TakeFace();
                    selectedSupportFace = selectedPlane.TakeFace();
                    var result = ScrewGeometryResolution.CreateResolved(selectedAxisFace, selectedSupportFace);
                    selectedAxisFace = null;
                    selectedSupportFace = null;
                    return result;
                }
                finally
                {
                    ComInterop.ReleaseOwned(ref selectedSupportFace);
                    ComInterop.ReleaseOwned(ref selectedAxisFace);
                }
            }
            finally
            {
                foreach (var candidate in planeCandidates) candidate.Dispose();
                foreach (var candidate in cylinderCandidates) candidate.Dispose();
                ComInterop.ReleaseOwned(ref facesObject);
                ComInterop.ReleaseOwned(ref bodyObject);
            }
        }

        private static CylinderFaceDescriptor? DescribeCylinder(
            int sourceIndex,
            Face face,
            Cylinder cylinder)
        {
            Array basePoint = new double[3];
            Array axisVector = new double[3];
            double radius = 0;
            cylinder.GetCylinderData(ref basePoint, ref axisVector, out radius);
            if (!TryReadVector(axisVector, scale: 1.0, out var axis))
            {
                return null;
            }
            var radiusMm = radius * MetresToMillimetres;
            var areaMm2 = face.Area * SquareMetresToSquareMillimetres;
            if (!IsPositiveFinite(radiusMm) || !IsPositiveFinite(areaMm2)) return null;
            GeometryVector3? root = TryReadVector(basePoint, MetresToMillimetres, out var baseVector)
                ? baseVector
                : (GeometryVector3?)null;
            return new CylinderFaceDescriptor(
                sourceIndex,
                radiusMm,
                areaMm2,
                axis,
                root);
        }

        private static PlaneFaceDescriptor? DescribePlane(
            int sourceIndex,
            Face face,
            Plane plane)
        {
            Array rootPoint = new double[3];
            Array normalVector = new double[3];
            plane.GetPlaneData(ref rootPoint, ref normalVector);
            if (!TryReadVector(normalVector, scale: 1.0, out var normal))
            {
                return null;
            }
            var areaMm2 = face.Area * SquareMetresToSquareMillimetres;
            if (!IsPositiveFinite(areaMm2)) return null;
            GeometryVector3? root = TryReadVector(rootPoint, MetresToMillimetres, out var rootVector)
                ? rootVector
                : (GeometryVector3?)null;
            return new PlaneFaceDescriptor(
                sourceIndex,
                areaMm2,
                normal,
                root);
        }

        private static bool TryReadVector(Array? values, double scale, out GeometryVector3 vector)
        {
            vector = default;
            if (values == null || values.Rank != 1 || values.Length < 3)
            {
                return false;
            }

            try
            {
                var lowerBound = values.GetLowerBound(0);
                vector = new GeometryVector3(
                    Convert.ToDouble(values.GetValue(lowerBound)) * scale,
                    Convert.ToDouble(values.GetValue(lowerBound + 1)) * scale,
                    Convert.ToDouble(values.GetValue(lowerBound + 2)) * scale);
                return IsFinite(vector.X) && IsFinite(vector.Y) && IsFinite(vector.Z);
            }
            catch (Exception ex) when (
                ex is InvalidCastException ||
                ex is FormatException ||
                ex is OverflowException ||
                ex is ArgumentException)
            {
                return false;
            }
        }

        private static string AppendTopologyWarning(string message, bool topologyIncomplete) =>
            topologyIncomplete
                ? message + " Parte de la topología no pudo describirse con seguridad."
                : message;

        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);

        private static bool IsPositiveFinite(double value) => value > 0 && IsFinite(value);

        private sealed class OwnedCylinderCandidate : IDisposable
        {
            private Face? _face;

            public OwnedCylinderCandidate(Face face, CylinderFaceDescriptor descriptor)
            {
                _face = face;
                Descriptor = descriptor;
            }

            public CylinderFaceDescriptor Descriptor { get; }

            public Face TakeFace()
            {
                var face = _face ?? throw new InvalidOperationException("La cara cilíndrica ya fue transferida.");
                _face = null;
                return face;
            }

            public void Dispose()
            {
                ComInterop.ReleaseOwned(ref _face);
            }
        }

        private sealed class OwnedPlaneCandidate : IDisposable
        {
            private Face? _face;

            public OwnedPlaneCandidate(Face face, PlaneFaceDescriptor descriptor)
            {
                _face = face;
                Descriptor = descriptor;
            }

            public PlaneFaceDescriptor Descriptor { get; }

            public Face TakeFace()
            {
                var face = _face ?? throw new InvalidOperationException("La cara plana ya fue transferida.");
                _face = null;
                return face;
            }

            public void Dispose()
            {
                ComInterop.ReleaseOwned(ref _face);
            }
        }
    }
}
