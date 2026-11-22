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

        /// <summary>
        /// Datos leídos de Solid Edge 2026 para un M8x30 (vástago Ø8×30, cabeza Ø13×5): el
        /// punto base del cilindro está en la punta (z=30) y el eje apunta fuera del material.
        /// </summary>
        [Theory]
        [InlineData(30.0)]
        [InlineData(0.0)]
        public void Classify_TornilloConBaseEnCualquierExtremo_EligeAsientoBajoCabezaYNoLaPunta(double baseZ)
        {
            var shank = Cylinder(1, radius: 4, length: 30, baseZ: baseZ);
            var headTop = Plane(2, normal: new GeometryVector3(0, 0, -1), rootZ: -5, areaMm2: 132.73, loops: 1);
            var seat = Plane(3, normal: new GeometryVector3(0, 0, -1), rootZ: 0, areaMm2: 82.47, loops: 2);
            var tip = Plane(4, normal: new GeometryVector3(0, 0, -1), rootZ: 30, areaMm2: 50.27, loops: 1);

            var result = _classifier.Classify(9.0, new[] { shank }, new[] { headTop, seat, tip });

            result.Status.Should().Be(FastenerGeometryStatus.Resolved);
            result.CylinderSourceIndex.Should().Be(1);
            result.PlaneSourceIndex.Should().Be(3);
        }

        /// <summary>
        /// M12 con dw mínimo de ISO 4017 (16,63 mm): la corona de apoyo (≈104 mm²) es menor que
        /// la sección del vástago (≈113 mm²), así que el área no sirve para distinguirla de la punta.
        /// </summary>
        [Fact]
        public void Classify_AsientoConCoronaMenorQueLaSeccion_SeIdentificaPorSusContornos()
        {
            var seatArea = Math.PI * ((8.315 * 8.315) - 36);
            var result = _classifier.Classify(
                13.0,
                new[] { Cylinder(1, radius: 6, length: 40, baseZ: 40) },
                new[]
                {
                    Plane(2, normal: ZAxis, rootZ: 0, areaMm2: seatArea, loops: 2),
                    Plane(3, normal: ZAxis, rootZ: 40, areaMm2: Math.PI * 36, loops: 1),
                });

            result.Status.Should().Be(FastenerGeometryStatus.Resolved);
            result.PlaneSourceIndex.Should().Be(2);
        }

        [Fact]
        public void Classify_SoloCaraExtremaSinContornoInterior_DevuelveUnsupported()
        {
            var result = _classifier.Classify(
                9.0,
                new[] { Cylinder(1, radius: 4, length: 30, baseZ: 30) },
                new[] { Plane(2, normal: ZAxis, rootZ: 30, areaMm2: Math.PI * 16, loops: 1) });

            result.Status.Should().Be(FastenerGeometryStatus.Unsupported);
        }

        [Fact]
        public void Classify_AmbosExtremosSinContornosConocidos_DevuelveAmbiguous()
        {
            var result = _classifier.Classify(
                9.0,
                new[] { Cylinder(1, radius: 4, length: 30, baseZ: 30) },
                new[]
                {
                    Plane(2, normal: ZAxis, rootZ: 0, areaMm2: 82.47),
                    Plane(3, normal: ZAxis, rootZ: 30, areaMm2: 50.27),
                });

            result.Status.Should().Be(FastenerGeometryStatus.Ambiguous);
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
            double rootZ,
            double areaMm2 = 100,
            int? loops = null)
        {
            return new PlaneFaceDescriptor(
                index,
                areaMm2,
                normal,
                new GeometryVector3(0, 0, rootZ),
                loops);
        }
    }
}
