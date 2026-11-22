using System;
using System.Collections.Generic;
using System.IO;

namespace SE_HardwareInserter.Core.Library
{
    /// <summary>Utilidades de ruta compatibles con net48.</summary>
    internal static class PathHelper
    {
        /// <summary>
        /// Devuelve la ruta relativa desde <paramref name="relativeTo"/> hasta <paramref name="path"/>.
        /// Compara segmentos sin distinguir mayúsculas en Windows y devuelve la ruta absoluta cuando
        /// las raíces son distintas.
        /// </summary>
        public static string GetRelativePath(string relativeTo, string path)
        {
            if (string.IsNullOrEmpty(relativeTo)) throw new ArgumentException("relativeTo vacía", nameof(relativeTo));
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path vacío", nameof(path));

            var basePath = Path.GetFullPath(relativeTo);
            var targetPath = Path.GetFullPath(path);
            var comparison = IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var baseRoot = Path.GetPathRoot(basePath) ?? string.Empty;
            var targetRoot = Path.GetPathRoot(targetPath) ?? string.Empty;

            if (!string.Equals(NormalizeRoot(baseRoot), NormalizeRoot(targetRoot), comparison))
            {
                return targetPath;
            }

            var baseSegments = SplitSegments(basePath.Substring(baseRoot.Length));
            var targetSegments = SplitSegments(targetPath.Substring(targetRoot.Length));
            var commonSegments = 0;
            var commonLimit = Math.Min(baseSegments.Count, targetSegments.Count);
            while (commonSegments < commonLimit &&
                   string.Equals(baseSegments[commonSegments], targetSegments[commonSegments], comparison))
            {
                commonSegments++;
            }

            var result = new List<string>();
            for (var i = commonSegments; i < baseSegments.Count; i++)
            {
                result.Add("..");
            }
            for (var i = commonSegments; i < targetSegments.Count; i++)
            {
                result.Add(targetSegments[i]);
            }

            return result.Count == 0
                ? "."
                : string.Join(Path.DirectorySeparatorChar.ToString(), result);
        }

        private static bool IsWindows => Path.DirectorySeparatorChar == '\\';

        private static string NormalizeRoot(string root) =>
            root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static List<string> SplitSegments(string path)
        {
            return new List<string>(path.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
