using UnityEngine;

namespace Nine.UI
{
    /// <summary>
    /// 配色と文字サイズ。落ち着いたダークの面に、明るい紙のカードを置く。
    /// 意味は色だけで伝えない(席は色＋「P1」の文字、勝ちは金色＋「勝ち」の札、など形や文字と組み合わせる)。
    /// </summary>
    public static class NineTheme
    {
        // ---- 面 ----
        public static readonly Color Background = Hex("0E1117");
        public static readonly Color BackgroundTop = Hex("171C28");
        public static readonly Color Surface = Hex("161A22");
        public static readonly Color Raised = Hex("1E2430");
        public static readonly Color RaisedHover = Hex("262E3C");
        public static readonly Color Line = Hex("2C3443");
        public static readonly Color LineStrong = Hex("3A4456");
        public static readonly Color Scrim = new Color(0.03f, 0.04f, 0.06f, 0.74f);

        // ---- 文字 ----
        public static readonly Color Text = Hex("EDF0F5");
        public static readonly Color Muted = Hex("8E98AA");
        public static readonly Color Faint = Hex("5D6779");

        // ---- 役割色 ----
        public static readonly Color Accent = Hex("6E8BFF");      // 主操作(開始・作成・出す)
        public static readonly Color AccentDeep = Hex("4C66D9");
        public static readonly Color Go = Hex("38C98B");          // 準備OK
        public static readonly Color Warn = Hex("F2B84B");
        public static readonly Color Danger = Hex("EF6461");
        public static readonly Color Gold = Hex("F4C95D");        // 勝者・得点
        public static readonly Color Spectate = Hex("A58BFF");    // 観戦

        // ---- カード ----
        public static readonly Color CardFace = Hex("F4F6FA");
        public static readonly Color CardInk = Hex("1A1F29");
        public static readonly Color CardUsed = Hex("232935");
        public static readonly Color CardUsedInk = Hex("4A5466");
        public static readonly Color CardPending = Hex("33405E");
        public static readonly Color CardEmpty = Hex("1B2029");

        // ---- 席の色(P1〜P6) ----
        public static readonly Color[] Seat =
        {
            Hex("5B9CFF"), Hex("F28B54"), Hex("43C59E"), Hex("C483FF"), Hex("E9C24A"), Hex("F06C9B"),
        };

        public static Color SeatColor(int seat) { return seat >= 0 ? Seat[seat % Seat.Length] : Spectate; }

        // ---- 文字サイズ(1920x1080 基準) ----
        public const float SizeSmall = 22;
        public const float SizeBody = 26;
        public const float SizeLabel = 30;
        public const float SizeTitle = 40;

        public static Color Hex(string hex)
        {
            Color c;
            ColorUtility.TryParseHtmlString("#" + hex, out c);
            return c;
        }

        public static Color A(Color c, float a) { return new Color(c.r, c.g, c.b, a); }

        public static string ToHex(Color c) { return "#" + ColorUtility.ToHtmlStringRGB(c); }
    }
}
