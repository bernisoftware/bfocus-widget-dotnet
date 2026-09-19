using System;
using System.Collections.Generic;
using System.Linq;

namespace Bfocus.Widget
{
    /// <summary>Estado da pílula de versão (<c>onReleaseNotesChanged</c>), conforme <c>scenarios.pill</c>.</summary>
    public sealed class ReleaseNotesState : IEquatable<ReleaseNotesState>
    {
        public const string NoVersionLabel = "—";

        public ReleaseNotesState(string label, bool dot, IReadOnlyList<string> bannerIds, string color)
        {
            Label = label;
            Dot = dot;
            BannerIds = bannerIds ?? Array.Empty<string>();
            Color = color;
        }

        /// <summary>Estado antes da primeira resposta.</summary>
        public static ReleaseNotesState Empty { get; } =
            new ReleaseNotesState(NoVersionLabel, false, Array.Empty<string>(), Protocol.BrandFallbackColor);

        /// <summary><c>v4.2.0</c> ou <c>—</c>.</summary>
        public string Label { get; }
        /// <summary>Ponto aceso: há novidade não vista.</summary>
        public bool Dot { get; }
        /// <summary>Fila do banner (já com a regra do servidor aplicada).</summary>
        public IReadOnlyList<string> BannerIds { get; }
        /// <summary>Cor da pílula: <c>primary_color</c> do tenant, senão a do produto, senão a do bFocus.</summary>
        public string Color { get; }

        public static ReleaseNotesState From(ReleaseNotesInfo? info, string? primaryColor = null)
        {
            info = info ?? new ReleaseNotesInfo();
            var color = FirstNonEmpty(primaryColor, info.Product?.Color) ?? Protocol.BrandFallbackColor;
            return new ReleaseNotesState(LabelFor(info.BadgeVersion), info.HasUnseen, info.BannerIds.ToArray(), color);
        }

        /// <summary><c>v{versão}</c> sem duplicar o "v" (<c>V3.0.0</c> → <c>v3.0.0</c>); <c>—</c> sem versão.</summary>
        public static string LabelFor(string? badgeVersion)
        {
            var v = (badgeVersion ?? string.Empty).Trim();
            if (v.Length > 0 && (v[0] == 'v' || v[0] == 'V')) v = v.Substring(1);
            return v.Length == 0 ? NoVersionLabel : "v" + v;
        }

        public ReleaseNotesState WithColor(string color) => new ReleaseNotesState(Label, Dot, BannerIds, color);

        internal static string? FirstNonEmpty(params string?[] values)
        {
            // `||` do JS: o backend manda '' quando não há cor.
            foreach (var v in values) if (!string.IsNullOrWhiteSpace(v)) return v;
            return null;
        }

        public bool Equals(ReleaseNotesState? other) =>
            other != null && other.Label == Label && other.Dot == Dot && other.Color == Color && other.BannerIds.SequenceEqual(BannerIds);

        public override bool Equals(object? obj) => Equals(obj as ReleaseNotesState);

        public override int GetHashCode() => (Label.GetHashCode() * 31) ^ Dot.GetHashCode() ^ BannerIds.Count;
    }
}
