using System;

namespace SE_HardwareInserter.SolidEdge
{
    /// <summary>Identidad y directorio persistente de un ensamblaje guardado.</summary>
    public sealed class AssemblyIdentity
    {
        internal AssemblyIdentity(string directory, string fullName)
        {
            Directory = !string.IsNullOrWhiteSpace(directory)
                ? directory
                : throw new ArgumentException("El directorio no puede estar vacío.", nameof(directory));
            FullName = !string.IsNullOrWhiteSpace(fullName)
                ? fullName
                : throw new ArgumentException("La identidad no puede estar vacía.", nameof(fullName));
        }

        /// <summary>Directorio devuelto por <c>AssemblyDocument.Path</c>.</summary>
        public string Directory { get; }

        /// <summary>Ruta completa devuelta por <c>AssemblyDocument.FullName</c>.</summary>
        public string FullName { get; }
    }
}
