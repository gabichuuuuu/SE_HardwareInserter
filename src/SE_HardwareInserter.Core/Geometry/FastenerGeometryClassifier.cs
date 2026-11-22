using System;
using System.Collections.Generic;

namespace SE_HardwareInserter.Core.Geometry
{
    /// <summary>Vector o punto tridimensional administrado, independiente de COM.</summary>
    public readonly struct GeometryVector3
    {
        public GeometryVector3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }

        public double Y { get; }

        public double Z { get; }

        internal bool IsFinite =>
            IsFiniteValue(X) && IsFiniteValue(Y) && IsFiniteValue(Z);

        internal double Length => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

        internal GeometryVector3 Normalize()
        {
            var length = Length;
            return new GeometryVector3(X / length, Y / length, Z / length);
        }

        internal static double Dot(GeometryVector3 first, GeometryVector3 second) =>
            (first.X * second.X) + (first.Y * second.Y) + (first.Z * second.Z);

        private static bool IsFiniteValue(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>Descripción administrada de una cara cilíndrica candidata.</summary>
    public sealed class CylinderFaceDescriptor
    {
        public CylinderFaceDescriptor(
            int sourceIndex,
            double radiusMm,
            double areaMm2,
            GeometryVector3 axis,
            GeometryVector3? basePoint)
        {
            SourceIndex = sourceIndex;
            RadiusMm = radiusMm;
            AreaMm2 = areaMm2;
            Axis = axis;
            BasePoint = basePoint;
        }

        public int SourceIndex { get; }

        public double RadiusMm { get; }

        public double AreaMm2 { get; }

        public GeometryVector3 Axis { get; }

        public GeometryVector3? BasePoint { get; }
    }

    /// <summary>Descripción administrada de una cara plana candidata.</summary>
    public sealed class PlaneFaceDescriptor
    {
        public PlaneFaceDescriptor(
            int sourceIndex,
            double areaMm2,
            GeometryVector3 normal,
            GeometryVector3? rootPoint)
            : this(sourceIndex, areaMm2, normal, rootPoint, loopCount: null)
        {
        }

        public PlaneFaceDescriptor(
            int sourceIndex,
            double areaMm2,
            GeometryVector3 normal,
            GeometryVector3? rootPoint,
            int? loopCount)
        {
            SourceIndex = sourceIndex;
            AreaMm2 = areaMm2;
            Normal = normal;
            RootPoint = rootPoint;
            LoopCount = loopCount;
        }

        /// <summary>
        /// Número de contornos de la cara, o null si no se conoce. Un asiento rodea el vástago y
        /// tiene al menos dos (exterior e interior); la punta del vástago es un disco con uno.
        /// </summary>
        public int? LoopCount { get; }

        public int SourceIndex { get; }

        public double AreaMm2 { get; }

        public GeometryVector3 Normal { get; }

        public GeometryVector3? RootPoint { get; }
    }

    /// <summary>Estado de la clasificación geométrica pura.</summary>
    public enum FastenerGeometryStatus
    {
        Unsupported,
        Ambiguous,
        Resolved,
    }

    /// <summary>Resultado puro que identifica los índices de la mejor pareja compatible.</summary>
    public sealed class FastenerGeometryClassification
    {
        internal FastenerGeometryClassification(
            FastenerGeometryStatus status,
            int cylinderSourceIndex,
            int planeSourceIndex,
            string message)
        {
            Status = status;
            CylinderSourceIndex = cylinderSourceIndex;
            PlaneSourceIndex = planeSourceIndex;
            Message = message;
        }

        public FastenerGeometryStatus Status { get; }

        public int CylinderSourceIndex { get; }

        public int PlaneSourceIndex { get; }

        public string Message { get; }
    }

    /// <summary>Clasificador conservador y determinista de eje y asiento de un elemento de fijación.</summary>
    /// <remarks>
    /// La proximidad supone que los puntos base/root recibidos describen extremos útiles de las
    /// superficies recortadas. El adaptador COM debe validar esta heurística con piezas reales en
    /// cada release; estos cálculos puros no demuestran por sí solos esa correspondencia geométrica.
    /// </remarks>
    public sealed class FastenerGeometryClassifier
    {
        private const double DiameterToleranceMm = 0.05;
        private const double MinimumDiameterRatio = 0.75;
        private const double MinimumDominanceRatio = 1.25;
        private const double MaximumParallelAngleRadians = Math.PI / 180.0;        private static readonly double MinimumParallelDot = Math.Cos(MaximumParallelAngleRadians);

        /// <summary>Clasifica las parejas cilindro/plano sin acceder a objetos COM.</summary>
        public FastenerGeometryClassification Classify(
            double holeDiameterMm,
            IReadOnlyList<CylinderFaceDescriptor> cylinders,
            IReadOnlyList<PlaneFaceDescriptor> planes)
        {
            if (!IsPositiveFinite(holeDiameterMm))
            {
                throw new ArgumentOutOfRangeException(nameof(holeDiameterMm));
            }
            if (cylinders == null) throw new ArgumentNullException(nameof(cylinders));
            if (planes == null) throw new ArgumentNullException(nameof(planes));

            var pairs = new List<CandidatePair>();
            var hasUnverifiedPair = false;
            foreach (var cylinder in cylinders)
            {
                if (!TryDescribeCylinder(cylinder, holeDiameterMm, out var axis, out var diameter, out var length))
                {
                    continue;
                }

                foreach (var plane in planes)
                {
                    var compatibility = TryScorePair(
                        cylinder,
                        axis,
                        diameter,
                        length,
                        plane,
                        holeDiameterMm,
                        out var score);
                    if (compatibility == PairCompatibility.Unverified)
                    {
                        hasUnverifiedPair = true;
                        continue;
                    }
                    if (compatibility == PairCompatibility.Incompatible)
                    {
                        continue;
                    }

                    pairs.Add(new CandidatePair(cylinder.SourceIndex, plane.SourceIndex, score));
                }
            }

            if (pairs.Count == 0)
            {
                if (hasUnverifiedPair)
                {
                    return new FastenerGeometryClassification(
                        FastenerGeometryStatus.Ambiguous,
                        -1,
                        -1,
                        "Hay candidatos compatibles, pero faltan puntos para demostrar que el plano está en un extremo del cilindro.");
                }
                return new FastenerGeometryClassification(
                    FastenerGeometryStatus.Unsupported,
                    -1,
                    -1,
                    "No hay una pareja cilindro/plano compatible con el diámetro y la orientación del agujero.");
            }

            if (hasUnverifiedPair)
            {
                return new FastenerGeometryClassification(
                    FastenerGeometryStatus.Ambiguous,
                    -1,
                    -1,
                    "Hay una pareja válida y otros candidatos cuya proximidad no puede demostrarse; use selección guiada.");
            }

            pairs.Sort((first, second) => second.Score.CompareTo(first.Score));
            var best = pairs[0];
            if (pairs.Count > 1 && best.Score < pairs[1].Score * MinimumDominanceRatio)
            {
                return new FastenerGeometryClassification(
                    FastenerGeometryStatus.Ambiguous,
                    -1,
                    -1,
                    "Hay varias parejas geométricas compatibles sin una dominante inequívoca; use selección guiada.");
            }

            return new FastenerGeometryClassification(
                FastenerGeometryStatus.Resolved,
                best.CylinderSourceIndex,
                best.PlaneSourceIndex,
                "El eje y el asiento forman una pareja compatible dominante.");
        }

        private static bool TryDescribeCylinder(
            CylinderFaceDescriptor cylinder,
            double holeDiameterMm,
            out GeometryVector3 normalizedAxis,
            out double diameterMm,
            out double estimatedLengthMm)
        {
            normalizedAxis = default;
            diameterMm = 0;
            estimatedLengthMm = 0;
            if (cylinder == null ||
                !IsPositiveFinite(cylinder.RadiusMm) ||
                !IsPositiveFinite(cylinder.AreaMm2) ||
                !cylinder.Axis.IsFinite ||
                cylinder.Axis.Length <= 1e-9)
            {
                return false;
            }

            diameterMm = cylinder.RadiusMm * 2.0;
            if (diameterMm < holeDiameterMm * MinimumDiameterRatio ||
                diameterMm > holeDiameterMm + DiameterToleranceMm)
            {
                return false;
            }

            estimatedLengthMm = cylinder.AreaMm2 / (2.0 * Math.PI * cylinder.RadiusMm);
            if (!IsPositiveFinite(estimatedLengthMm))
            {
                return false;
            }

            normalizedAxis = cylinder.Axis.Normalize();
            return true;
        }

        private static PairCompatibility TryScorePair(
            CylinderFaceDescriptor cylinder,
            GeometryVector3 normalizedAxis,
            double diameterMm,
            double estimatedLengthMm,
            PlaneFaceDescriptor plane,
            double holeDiameterMm,
            out double score)
        {
            score = 0;
            if (plane == null ||
                !IsPositiveFinite(plane.AreaMm2) ||
                !plane.Normal.IsFinite ||
                plane.Normal.Length <= 1e-9)
            {
                return PairCompatibility.Incompatible;
            }

            var normalizedNormal = plane.Normal.Normalize();
            var parallelDot = Math.Abs(GeometryVector3.Dot(normalizedAxis, normalizedNormal));
            if (parallelDot < MinimumParallelDot)
            {
                return PairCompatibility.Incompatible;
            }

            if (!cylinder.BasePoint.HasValue || !plane.RootPoint.HasValue ||
                !cylinder.BasePoint.Value.IsFinite || !plane.RootPoint.Value.IsFinite)
            {
                return PairCompatibility.Unverified;
            }

            // Un asiento rodea el vástago (contorno interior). La punta es un disco de un solo
            // contorno y no puede apoyar sobre la pieza agujereada. El área no discrimina: con el
            // dw mínimo de ISO 4017 desde M12 la corona de apoyo es menor que la sección.
            if (plane.LoopCount.HasValue && plane.LoopCount.Value < 2)
            {
                return PairCompatibility.Incompatible;
            }

            var proximityScore = 0.0;
            {
                // Solid Edge 2026 puede situar el punto base en cualquiera de los dos extremos y
                // orientar el eje hacia fuera del material: el otro extremo queda a ±longitud.
                var delta = Subtract(plane.RootPoint.Value, cylinder.BasePoint.Value);
                var projectedDistance = Math.Abs(GeometryVector3.Dot(delta, normalizedAxis));
                var distanceToEnd = Math.Min(
                    projectedDistance,
                    Math.Abs(projectedDistance - estimatedLengthMm));
                var allowedDistance = Math.Max(0.25, Math.Min(1.0, estimatedLengthMm * 0.10));
                if (distanceToEnd > allowedDistance)
                {
                    return PairCompatibility.Incompatible;
                }
                proximityScore = 1.0 - (distanceToEnd / allowedDistance);
            }

            var diameterError = Math.Abs(holeDiameterMm - diameterMm);
            var diameterScore = 1.0 - Math.Min(1.0, diameterError / holeDiameterMm);
            var slenderness = estimatedLengthMm / Math.Max(diameterMm, 0.001);
            var lengthScore = slenderness / (1.0 + slenderness);
            score = (4.0 * diameterScore) + (2.0 * lengthScore) + (3.0 * proximityScore);
            return PairCompatibility.Compatible;
        }

        private static GeometryVector3 Subtract(GeometryVector3 first, GeometryVector3 second) =>
            new GeometryVector3(first.X - second.X, first.Y - second.Y, first.Z - second.Z);

        private static bool IsPositiveFinite(double value) =>
            value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);

        private sealed class CandidatePair
        {
            public CandidatePair(int cylinderSourceIndex, int planeSourceIndex, double score)
            {
                CylinderSourceIndex = cylinderSourceIndex;
                PlaneSourceIndex = planeSourceIndex;
                Score = score;
            }

            public int CylinderSourceIndex { get; }

            public int PlaneSourceIndex { get; }

            public double Score { get; }
        }

        private enum PairCompatibility
        {
            Incompatible,
            Unverified,
            Compatible,
        }
    }
}
