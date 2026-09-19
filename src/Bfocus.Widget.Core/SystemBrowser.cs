using System;
using System.Diagnostics;

namespace Bfocus.Widget
{
    /// <summary>Abre URLs no navegador padrão (links externos e o modo navegador).</summary>
    public static class SystemBrowser
    {
        /// <summary>Abre com <c>UseShellExecute</c>. Recusa esquemas perigosos (file:, javascript:…).</summary>
        public static bool Open(string url)
        {
            if (!EmbedOrigin.IsExternalSafe(url)) return false;
            try
            {
                using (Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })) { }
                return true;
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException || e is PlatformNotSupportedException)
            {
                return false;
            }
        }
    }
}
