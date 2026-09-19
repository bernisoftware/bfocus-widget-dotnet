using System;

namespace Bfocus.Widget
{
    /// <summary><c>onBadgeChanged(label)</c>: <c>''</c> | <c>'•'</c> | <c>'N'</c> | <c>'99+'</c>.</summary>
    public sealed class BadgeChangedEventArgs : EventArgs
    {
        public BadgeChangedEventArgs(string label) { Label = label; }
        public string Label { get; }
    }

    /// <summary><c>onReleaseNotesChanged({label, dot, bannerIds})</c>.</summary>
    public sealed class ReleaseNotesChangedEventArgs : EventArgs
    {
        public ReleaseNotesChangedEventArgs(ReleaseNotesState state) { State = state; }
        public ReleaseNotesState State { get; }
    }

    /// <summary><c>onError(code, detail)</c>.</summary>
    public sealed class BFocusErrorEventArgs : EventArgs
    {
        public BFocusErrorEventArgs(string code, string? detail) { Code = code; Detail = detail; }
        public string Code { get; }
        public string? Detail { get; }
    }

    /// <summary>Cor do tenant (<c>primary_color</c> / <c>bfocus:branding</c>) para pintar o botão.</summary>
    public sealed class PrimaryColorChangedEventArgs : EventArgs
    {
        public PrimaryColorChangedEventArgs(string color) { Color = color; }
        public string Color { get; }
    }
}
