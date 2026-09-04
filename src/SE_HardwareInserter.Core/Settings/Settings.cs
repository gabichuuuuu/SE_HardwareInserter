#nullable enable

using System.Runtime.Serialization;

namespace SE_HardwareInserter.Core.Preferences
{
    /// <summary>
    /// Preferencias persistentes del add-in. La lectura, escritura y migración pertenecen a
    /// <see cref="SettingsStore"/> para mantener este tipo como un DTO serializable.
    /// </summary>
    [DataContract]
    public sealed class Settings
    {
        /// <summary>Carpeta raíz de la librería de hardware.</summary>
        [DataMember(Name = "rootPath", Order = 1)]
        public string RootPath { get; set; } = string.Empty;
    }
}
