using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GeoScheme.AutoCAD
{
    /// <summary>
    /// pdfium.dll лежит рядом с плагином в подпапке x64, а AutoCAD ищет
    /// нативные библиотеки в своей папке. Поэтому грузим явно по полному пути —
    /// дальше P/Invoke по имени "pdfium.dll" находит уже загруженный модуль.
    /// </summary>
    internal static class NativePdfium
    {
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string fileName);

        private static bool _loaded;

        public static void EnsureLoaded()
        {
            if (_loaded)
                return;

            var dir = Path.GetDirectoryName(typeof(NativePdfium).Assembly.Location);
            if (string.IsNullOrEmpty(dir))
            {
                _loaded = true;
                return;
            }

            var native = Path.Combine(dir, "x64", "pdfium.dll");
            if (File.Exists(native))
                LoadLibrary(native);

            _loaded = true;
        }
    }
}
