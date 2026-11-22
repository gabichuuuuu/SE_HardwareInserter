using System;
using System.Runtime.InteropServices;
using SolidEdgeFramework;

namespace SE_HardwareInserter.SolidEdge
{
    /// <summary>Operaciones centralizadas para RCW adquiridos explícitamente por el add-in.</summary>
    internal static class ComInterop
    {
        /// <summary>
        /// Libera exactamente una referencia administrada y anula primero el campo para que la
        /// operación sea idempotente. No debe usarse con <c>Application</c> recibida de Solid Edge.
        /// </summary>
        public static void ReleaseOwned<T>(ref T? value) where T : class
        {
            var owned = value;
            value = null;
            if (owned == null || !Marshal.IsComObject(owned)) return;

            try
            {
                Marshal.ReleaseComObject(owned);
            }
            catch (InvalidComObjectException)
            {
                // Otro alias ya invalidó el RCW; el campo local permanece anulado.
            }
        }

        /// <summary>Libera los RCW contenidos en un SAFEARRAY devuelto por la interop.</summary>
        public static void ReleaseOwnedItems(Array? items)
        {
            if (items == null) return;
            foreach (var item in items)
            {
                object? owned = item;
                ReleaseOwned(ref owned);
            }
        }

        /// <summary>Compara la identidad COM mediante el IUnknown canónico.</summary>
        public static bool HasSameIdentity(object first, object second)
        {
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) throw new ArgumentNullException(nameof(second));
            if (!Marshal.IsComObject(first) || !Marshal.IsComObject(second))
            {
                return ReferenceEquals(first, second);
            }

            IntPtr firstUnknown = IntPtr.Zero;
            IntPtr secondUnknown = IntPtr.Zero;
            try
            {
                firstUnknown = Marshal.GetIUnknownForObject(first);
                secondUnknown = Marshal.GetIUnknownForObject(second);
                return firstUnknown == secondUnknown;
            }
            finally
            {
                if (secondUnknown != IntPtr.Zero) Marshal.Release(secondUnknown);
                if (firstUnknown != IntPtr.Zero) Marshal.Release(firstUnknown);
            }
        }

        /// <summary>Obtiene con propiedad local la occurrence superior de una Reference.</summary>
        public static object GetOwnedTopOccurrence(Reference reference)
        {
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            object? topOccurrence = null;
            Array path = Array.CreateInstance(typeof(object), 0);
            try
            {
                reference.GetOccurrencesInPath(out topOccurrence!, out _, out _, ref path);
                if (topOccurrence == null)
                {
                    throw new InvalidOperationException(
                        "Solid Edge no devolvió la occurrence superior de la referencia.");
                }

                var result = topOccurrence;
                topOccurrence = null;
                return result;
            }
            finally
            {
                ReleaseOwnedItems(path);
                ReleaseOwned(ref topOccurrence);
            }
        }
    }
}
