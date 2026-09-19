using System;
using System.Collections.Generic;
using System.Linq;

namespace Bfocus.Widget.Tests.Support
{
    /// <summary>Host de mentira: registra o que o núcleo pediu, sem WebView.</summary>
    internal sealed class FakeHost : IBFocusWidgetHost
    {
        private readonly object _lock = new object();
        private readonly List<string> _calls = new List<string>();

        public BFocusWidget? Widget { get; private set; }
        public bool WebViewAvailable { get; set; } = true;
        public List<(WidgetSurface Surface, string Url, bool ForceLoad)> Shown { get; } = new();
        public List<(WidgetSurface Surface, string Script)> Scripts { get; } = new();
        public List<string> External { get; } = new();
        public List<(string Url, string FileName)> Downloads { get; } = new();
        public List<(string Title, string Body, Action OnClick)> Notifications { get; } = new();

        public IReadOnlyList<string> Calls { get { lock (_lock) return _calls.ToList(); } }

        public void Attach(BFocusWidget widget) => Widget = widget;

        public bool IsWebViewAvailable => WebViewAvailable;

        public void ShowSurface(WidgetSurface surface, string url, bool forceLoad)
        {
            lock (_lock)
            {
                Shown.Add((surface, url, forceLoad));
                _calls.Add($"show:{surface}:{(forceLoad ? "load" : "keep")}");
            }
        }

        public void HideSurface(WidgetSurface surface) { lock (_lock) _calls.Add($"hide:{surface}"); }

        public void DestroySurface(WidgetSurface surface) { lock (_lock) _calls.Add($"destroy:{surface}"); }

        public void ExecuteScript(WidgetSurface surface, string script)
        {
            lock (_lock)
            {
                Scripts.Add((surface, script));
                _calls.Add($"script:{surface}");
            }
        }

        public void MarkReady(WidgetSurface surface) { lock (_lock) _calls.Add($"ready:{surface}"); }

        public void OpenExternal(string url) { lock (_lock) { External.Add(url); _calls.Add("external"); } }

        public void Download(string url, string fileName) { lock (_lock) { Downloads.Add((url, fileName)); _calls.Add("download"); } }

        public void ShowNotification(string title, string body, Action onClick)
        {
            lock (_lock) { Notifications.Add((title, body, onClick)); _calls.Add("notify"); }
        }

        public void ClearWebViewData(WidgetIdentity identity) { lock (_lock) _calls.Add($"clear:{identity.Origin.Value}"); }

        /// <summary>Simula o embed mandando uma mensagem pela ponte (origem certa).</summary>
        public void Deliver(WidgetSurface surface, string json, string? sourceUrl = null)
        {
            var origin = Widget!.Identity!.Origin.Value;
            var page = surface == WidgetSurface.Tickets ? "/v1/embed.html" : "/v1/release-notes.html";
            Widget.HandleEmbedMessage(surface, sourceUrl ?? origin + page + "#host=native", json);
        }

        public List<string> ScriptTypes(WidgetSurface surface)
        {
            lock (_lock)
                return Scripts.Where(s => s.Surface == surface)
                    .Select(s => System.Text.RegularExpressions.Regex.Match(s.Script, "\"type\":\"([^\"]+)\"").Groups[1].Value)
                    .ToList();
        }
    }
}
