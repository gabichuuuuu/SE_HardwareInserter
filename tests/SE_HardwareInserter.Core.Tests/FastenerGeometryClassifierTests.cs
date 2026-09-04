using System;
using FluentAssertions;
using SE_HardwareInserter.Core.Geometry;
using Xunit;

namespace SE_HardwareInserter.Core.Tests
{
    public class FastenerGeometryClassifierTests
    {
        private readonly FastenerGeometryClassifier _classifier = new FastenerGeometryClassifier();

        [Fact]
        public void Classify_ParejaUnicaCompatible_ResuelveIndices()
        {
            var result = _classifier.Classify(
                6.05,
                new[] { Cylinder(10, radius: 3, length: 20, baseZ: 0) },
                new[] { Plane(20, normal: ZAxis, rootZ: 0) });

            result.Status.Should().Be(FastenerGeometryStatus.Resolved);
            result.CylinderSourceIndex.Should().Be(10);
            result.PlaneSourceIndex.Should().Be(20);
        }

        [Fact]
        public void Classify_DosParejasEquivalentes_DevuelveAmbiguous()
        {
            var result = _classifier.Classify(
                6.05,
                new[]
                {
                    Cylinder(1, radius: 3, length: 20, baseZ: 0),
                    Cylinder(2, radius: 3, length: 20, baseZ: 100),
                },
                new[]
                {
                    Plane(3, normal: ZAxis, rootZ: 0),
                    Plane(4, normal: ZAxis, rootZ: 100),
                });

            result.Status.Should().Be(FastenerGeometryStatus.Ambiguous);
        }

        [Fact]
        public void Classify_CilindroDeCabezaConMayorArea_EligeVastagoCompatibleConAgujero()
        {
            var result = _classifier.Classify(
                6.05,
                new[]
                {
                    Cylinder(1, radius: 3, length: 12, baseZ: 0),
                    Cylinder(2, radius: 5, length: 30, baseZ: 0),
                },
                new[] { Plane(3, normal: ZAxis, rootZ: 0) });

            result.Status.Should().Be(FastenerGeometryStatus.Resolved);
            result.CylinderSourceIndex.Should().Be(1);
        }

        [Fact]
        public void Classify_VastagoCortoConParejaUnica_SeMantieneCompatible()
        {
            var result = _classifier.Classify(
                6.0,
                new[] { Cylinder(1, radius: 3, length: 1, baseZ: 0) },
                new[] { Plane(2, normal: ZAxis, rootZ: 1) });

            result.Status.Should().Be(FastenerGeometryStatus.Resolved);
        }

        [Fact]
        public void Classify_AvellanadoSinPlanoEnExtremo_DevuelveUnsupported()
        {
            var result = _classifier.Classify(
                6.0,
                new[] { Cylinder(1, radius: 3, length: 10, baseZ: 0) },
                new[] { Plane(2, normal: ZAxis, rootZ: 5) });

            result.Status.Should().Be(FastenerGeometryStatus.Unsupported);
        }

        [Fact]
        public void Classify_SinCilindros_DevuelveUnsupported()
        {
            var result = _classifier.Classify(
                6.0,
                Array.Empty<CylinderFaceDescriptor>(),
                new[] { Plane(1, normal: ZAxis, rootZ: 0) });

            result.Status.Should().Be(FastenerGeometryStatus.Unsupported);
        }

        [Fact]
        public void Classify_SinPlanos_DevuelveUnsupported()
        {
            var result = _classifier.Classify(
                6.0,
                new[] { Cylinder(1, radius: 3, length: 10, baseZ: 0) },
                Array.Empty<PlaneFaceDescriptor>());

            result.Status.Should().Be(FastenerGeometryStatus.Unsupported);
        }

        [Fact]
        public void Classify_DiametroMayorQueAgujeroMasTolerancia_DevuelveUnsupported()
        {
            var result = _classifier.Classify(
                6.0,
                new[] { Cylinder(1, radius: 3.026, length: 10, baseZ: 0) },
                new[] { Plane(2, normal: ZAxis, rootZ: 0) });

            result.Status.Should().Be(FastenerGeometryStatus.Unsupported);
        }

        [Fact]
        public void Classify_NormalAUnAnguloMayorDeUnGrado_DevuelveUnsupported()
        {
            var angle = 2.0 * Math.PI / 180.0;
            var tiltedNormal = new GeometryVector3(Math.Sin(angle), 0, Math.Cos(angle));

            var result = _classifier.Classify(
                6.0,
                new[] { Cylinder(1, radius: 3, length: 10, baseZ: 0) },
                new[] { Plane(2, normal: tiltedNormal, rootZ: 0) });

            result.Status.Should().Be(FastenerGeometryStatus.Unsupported);
        }

        [Fact]
        public void Classify_VectoresNoUnitariosSinPuntos_NoResuelveSinProximidadDemostrable()
        {
            var cylinder = new CylinderFaceDescriptor(
                1,
                radiusMm: 3,
                areaMm2: 2.0 * Math.PI * 3 * 10,
                new GeometryVector3(0, 0, 5),
                basePoint: null);
            var plane = new PlaneFaceDescriptor(
                2,
                areaMm2: 100,
                new GeometryVector3(0, 0, -2),
                rootPoint: null);

            var result = _classifier.Classify(6.0, new[] { cylinder }, new[] { plane });

            result.Status.Should().Be(FastenerGeometryStatus.Ambiguous);
        }

        [Fact]
        public void Classify_CilindroMuyInferiorAlAgujero_NoResuelve()
        {
            var result = _classifier.Classify(
                20.0,
                new[] { Cylinder(1, radius: 1, length: 10, baseZ: 0) },
                new[] { Plane(2, normal: ZAxis, rootZ: 0) });

            result.Status.Should().Be(FastenerGeometryStatus.Unsupported);
        }

        private static GeometryVector3 ZAxis => new GeometryVector3(0, 0, 1);

        private static CylinderFaceDescriptor Cylinder(
            int index,
            double radius,
            double length,
            double baseZ)
        {
            var lateralArea = 2.0 * Math.PI * radius * length;
            return new CylinderFaceDescriptor(
                index,
                radius,
                lateralArea,
                ZAxis,
                new GeometryVector3(0, 0, baseZ));
        }

        private static PlaneFaceDescriptor Plane(
            int index,
            GeometryVector3 normal,
            double rootZ)
        {
            return new PlaneFaceDescriptor(
                index,
                areaMm2: 100,
                normal,
                new GeometryVector3(0, 0, rootZ));
        }
    }
}
