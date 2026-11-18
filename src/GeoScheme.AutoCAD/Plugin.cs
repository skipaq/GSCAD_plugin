using System;
using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;

[assembly: ExtensionApplication(typeof(GeoScheme.AutoCAD.Plugin))]
[assembly: CommandClass(typeof(GeoScheme.AutoCAD.Commands))]

namespace GeoScheme.AutoCAD
{
    /// <summary>
    /// Точка входа плагина. Регистрирует вкладку в ленте после того,
    /// как AutoCAD построил свой интерфейс.
    /// </summary>
    public sealed class Plugin : IExtensionApplication
    {
        public const string Version = "0.1.0";
        public const string TabId = "GEO_SCHEME_TAB";

        private bool _ribbonHooked;

        public void Initialize()
        {
            try
            {
                if (ComponentManager.Ribbon != null)
                {
                    RibbonBuilder.EnsureTab();
                }
                else
                {
                    ComponentManager.ItemInitialized += OnItemInitialized;
                    _ribbonHooked = true;
                }
            }
            catch (System.Exception ex)
            {
                // accoreconsole и другие headless-режимы: лента недоступна, это нормально.
                WriteLine("GeoScheme: лента недоступна ({0})", ex.GetType().Name);
            }
        }

        public void Terminate()
        {
            if (_ribbonHooked)
                ComponentManager.ItemInitialized -= OnItemInitialized;
        }

        private void OnItemInitialized(object sender, RibbonItemEventArgs e)
        {
            if (ComponentManager.Ribbon == null)
                return;

            ComponentManager.ItemInitialized -= OnItemInitialized;
            _ribbonHooked = false;
            RibbonBuilder.EnsureTab();
        }

        internal static void WriteLine(string format, params object[] args)
        {
            var doc = Application.DocumentManager?.MdiActiveDocument;
            if (doc == null)
                return;

            doc.Editor.WriteMessage("\n" + string.Format(format, args));
        }
    }
}
