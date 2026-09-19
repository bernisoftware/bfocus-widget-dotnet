namespace Bfocus.Widget
{
    /// <summary>
    /// Ícones do widget web (viewBox 24×24), para os toolkits desenharem igual:
    /// WPF/MAUI usam o path SVG direto; WinForms usa os pontos equivalentes.
    /// </summary>
    public static class WidgetIcons
    {
        /// <summary>Balão do botão (traço 1,8, cantos arredondados) — launcher/bootstrap.ts.</summary>
        public const string ChatBubblePath =
            "M3 11c0-4.4 4-8 9-8s9 3.6 9 8c0 4.4-4 8-9 8-1 0-2-.1-2.9-.4L4 21l1.4-4.4C4 15.2 3 13.2 3 11z";

        /// <summary>Os três pontos do balão: centros (9,11), (12,11), (15,11), raio 1.</summary>
        public static readonly float[] ChatDotsX = { 9f, 12f, 15f };

        /// <summary>✕ do botão aberto (traço 2).</summary>
        public const string ClosePath = "M6 6L18 18M6 18L18 6";

        /// <summary>Estrela da pílula de versão (preenchida) — launcher-release-notes/bootstrap.ts.</summary>
        public const string StarPath = "M12 2l2.4 7.4H22l-6 4.4 2.3 7.4-6.3-4.6-6.3 4.6L7.9 13.8 2 9.4h7.6z";

        /// <summary>A mesma estrela como polígono (x0,y0,x1,y1…).</summary>
        public static readonly float[] StarPolygon =
        {
            12f, 2f, 14.4f, 9.4f, 22f, 9.4f, 16f, 13.8f, 18.3f, 21.2f,
            12f, 16.6f, 5.7f, 21.2f, 7.9f, 13.8f, 2f, 9.4f, 9.6f, 9.4f,
        };

        /// <summary>Vermelho do badge e do ponto (<c>#ef4444</c>).</summary>
        public const string BadgeColor = "#EF4444";
    }
}
