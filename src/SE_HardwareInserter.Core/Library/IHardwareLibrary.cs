using System;
using System.Collections.Generic;

namespace SE_HardwareInserter.Core.Library
{
    /// <summary>Describe un error recuperable al observar o refrescar una librería.</summary>
    public sealed class LibraryErrorEventArgs : EventArgs
    {
        public LibraryErrorEventArgs(string rootPath, Exception exception)
        {
            RootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
            Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        }

        /// <summary>Raíz cuyo watcher o escaneo produjo el error.</summary>
        public string RootPath { get; }

        /// <summary>Error que impidió completar la actualización.</summary>
        public Exception Exception { get; }
    }

    /// <summary>
    /// Entrada de la librería de hardware descubierta escaneando el sistema de archivos:
    /// un archivo <c>.par</c> con su ruta completa, su categoría (subcarpeta inmediata) y su nombre.
    /// No se parsea ni infiere Tipo/Métrica/Longitud del nombre: el catálogo muestra los archivos
    /// tal cual existen en disco, agrupados por la carpeta que los contiene.
    /// </summary>
    public sealed class LibraryItem
    {
        public LibraryItem(string fullPath, string relativePath, string category, string filename)
        {
            FullPath = fullPath ?? throw new ArgumentNullException(nameof(fullPath));
            RelativePath = relativePath ?? throw new ArgumentNullException(nameof(relativePath));
            Category = category ?? throw new ArgumentNullException(nameof(category));
            Filename = filename ?? throw new ArgumentNullException(nameof(filename));
        }

        /// <summary>Ruta absoluta al archivo <c>.par</c> en disco.</summary>
        public string FullPath { get; }

        /// <summary>Ruta relativa a <see cref="IHardwareLibrary.RootPath"/>, usando separador de directorio del SO.</summary>
        public string RelativePath { get; }

        /// <summary>
        /// Nombre de la subcarpeta inmediata que contiene el archivo, relativa a <see cref="IHardwareLibrary.RootPath"/>.
        /// Sirve como agrupación/categoría natural en el árbol del diálogo. Si el archivo está en la raíz, es "(Raíz)".
        /// </summary>
        public string Category { get; }

        /// <summary>Nombre del archivo con extensión (ej. <c>DIN912_M6x16.par</c>).</summary>
        public string Filename { get; }

        public override string ToString() => RelativePath;
    }

    /// <summary>
    /// Librería de hardware (tornillería, anchors, etc.) descubierta dinámicamente desde una carpeta raíz
    /// en el sistema de archivos. No hay catálogo JSON ni Excel: el índice se reconstruye leyendo los
    /// <c>.par</c> existentes, y se notifica vía <see cref="Changed"/> cuando se añaden o eliminan archivos.
    /// </summary>
    public interface IHardwareLibrary : IDisposable
    {
        /// <summary>Carpeta raíz de la librería que se escanea recursivamente en busca de <c>.par</c>.</summary>
        string RootPath { get; }

        /// <summary>Lista inmutable de los <c>.par</c> descubiertos, ordenada por ruta relativa.</summary>
        IReadOnlyList<LibraryItem> Items { get; }

        /// <summary>Último error recuperable al observar o refrescar la librería.</summary>
        Exception? LastError { get; }

        /// <summary>
        /// Se lanza cuando el contenido de <see cref="RootPath"/> cambia (nuevo <c>.par</c>, borrado, renombrado).
        /// Los argumentos del evento son <c>EventArgs.Empty</c>; el receptor debe releer <see cref="Items"/>.
        /// </summary>
        event EventHandler? Changed;

        /// <summary>Se lanza cuando un error posterior a la construcción impide actualizar el snapshot.</summary>
        event EventHandler<LibraryErrorEventArgs>? Error;

        /// <summary>Reescanea <see cref="RootPath"/> y refresca <see cref="Items"/> de forma síncrona.</summary>
        void Refresh();

        /// <summary>Cambia la carpeta raíz y reescanea. Dispara <see cref="Changed"/> si la lista resulta distinta.</summary>
        void SetRootPath(string rootPath);
    }
}
