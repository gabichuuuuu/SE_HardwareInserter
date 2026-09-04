using System;
using SolidEdgeFramework;
using SolidEdgeGeometry;

namespace SE_HardwareInserter.SolidEdge
{
    /// <summary>Operaciones de geometría que respetan las firmas SAFEARRAY de la interop 220.2.0.</summary>
    internal static class GeometryInterop
    {
        public static Face GetOwnedFace(Reference reference)
        {
            if (reference == null) throw new ArgumentNullException(nameof(reference));

            object? entity = null;
            try
            {
                entity = reference.Object;
                if (!(entity is Face face))
                {
                    throw new InvalidOperationException(
                        "La referencia seleccionada no apunta a una cara de Solid Edge.");
                }

                entity = null;
                return face;
            }
            finally
            {
                ComInterop.ReleaseOwned(ref entity);
            }
        }

        public static double GetCylinderDiameterMm(Face face)
        {
            object? geometry = null;
            try
            {
                geometry = face.Geometry;
                if (!(geometry is Cylinder cylinder))
                {
                    throw new InvalidOperationException("La referencia axial no apunta a una cara cilíndrica.");
                }

                const double MetresToMillimetres = 1000.0;
                var diameter = cylinder.Radius * 2.0 * MetresToMillimetres;
                if (double.IsNaN(diameter) || double.IsInfinity(diameter) || diameter <= 0)
                {
                    throw new InvalidOperationException("La cara cilíndrica devolvió un radio no válido.");
                }
                return diameter;
            }
            finally
            {
                ComInterop.ReleaseOwned(ref geometry);
            }
        }

        public static void EnsureCylinder(Face face)
        {
            EnsureGeometry<Cylinder>(face, "La referencia axial no apunta a una cara cilíndrica.");
        }

        public static void EnsurePlane(Face face)
        {
            EnsureGeometry<Plane>(face, "La referencia de apoyo no apunta a una cara plana.");
        }

        public static Array GetPointOnFace(Face face)
        {
            if (face == null) throw new ArgumentNullException(nameof(face));

            Array min = new double[2];
            Array max = new double[2];
            face.GetParamRange(ref min, ref max);
            if (min.Rank != 1 || max.Rank != 1 || min.Length < 2 || max.Length < 2)
            {
                throw new InvalidOperationException("Solid Edge devolvió un rango paramétrico no válido.");
            }

            Array parameters = new double[2];
            parameters.SetValue(Midpoint(ReadDouble(min, 0), ReadDouble(max, 0)), 0);
            parameters.SetValue(Midpoint(ReadDouble(min, 1), ReadDouble(max, 1)), 1);

            Array points = new double[3];
            face.GetPointAtParam(1, ref parameters, ref points);
            if (points.Rank != 1 || points.Length < 3)
            {
                throw new InvalidOperationException("Solid Edge no devolvió un punto XYZ válido sobre la cara.");
            }

            var result = new double[3];
            for (var index = 0; index < result.Length; index++)
            {
                var value = ReadDouble(points, index);
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    throw new InvalidOperationException("Solid Edge devolvió coordenadas no finitas sobre la cara.");
                }
                result[index] = value;
            }
            return result;
        }

        private static void EnsureGeometry<TGeometry>(Face face, string message) where TGeometry : class
        {
            if (face == null) throw new ArgumentNullException(nameof(face));
            object? geometry = null;
            try
            {
                geometry = face.Geometry;
                if (!(geometry is TGeometry)) throw new InvalidOperationException(message);
            }
            finally
            {
                ComInterop.ReleaseOwned(ref geometry);
            }
        }

        private static double ReadDouble(Array values, int offset)
        {
            var index = values.GetLowerBound(0) + offset;
            return Convert.ToDouble(values.GetValue(index));
        }

        private static double Midpoint(double first, double second) => first + ((second - first) / 2.0);
    }
}
