using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Nine.UI
{
    public enum BtnStyle { Primary, Go, Secondary, Ghost, Danger, Quiet }

    /// <summary>
    /// コードで組み立てたボタン。押せないときはボタン全体(面・枠・文字)を同じだけ薄くする。
    /// (面だけを暗くすると、色の付いたボタンで文字と枠だけが浮いて見えるため)
    /// </summary>
    public sealed class NineButton : MonoBehaviour
    {
        public Button Button;
        public Image Face;
        public Image Ring;
        public TextMeshProUGUI Label;
        public BtnStyle Style;
        CanvasGroup group;
        int state = -1;

        public void Init(Button b, Image face, Image ring, TextMeshProUGUI label)
        {
            Button = b; Face = face; Ring = ring; Label = label;
            group = gameObject.AddComponent<CanvasGroup>();
            Apply(true);
        }

        void OnEnable() { Apply(true); }
        void LateUpdate() { Apply(false); }

        void Apply(bool force)
        {
            if (Button == null || group == null) return;
            int now = Button.IsInteractable() ? 1 : 0;
            if (now == state && !force) return;
            state = now;
            group.alpha = now == 1 ? 1f : 0.38f;
        }

        public string Text
        {
            get { return Label != null ? Label.text : ""; }
            set { if (Label != null) Label.text = value; }
        }

        public bool Interactable
        {
            get { return Button.interactable; }
            set { Button.interactable = value; }
        }

        public void SetStyle(BtnStyle style) { Style = style; NineUi.ApplyStyle(this, style); }

        public RectTransform Rt { get { return (RectTransform)transform; } }
    }

    /// <summary>モーダルの外側(暗い覆い)をクリックしたら閉じる。押し始めと離した位置の両方が外のときだけ。</summary>
    public sealed class NineBackdrop : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
    {
        public RectTransform Card;
        public Action OnOutside;
        bool pressedOutside;

        bool Outside(PointerEventData e)
        {
            return Card == null || !RectTransformUtility.RectangleContainsScreenPoint(Card, e.position, e.pressEventCamera ?? e.enterEventCamera);
        }

        public void OnPointerDown(PointerEventData e) { pressedOutside = Outside(e); }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            if (pressedOutside && Outside(e) && OnOutside != null) OnOutside();
            pressedOutside = false;
        }
    }

    /// <summary>
    /// 角丸の半径を、要素の大きさに合わせて「短辺の半分」までに抑える。
    /// 大きさが変わるたびに掛け直すので、札(999指定の丸い両端)もスライダーの溝も常に半円の端になる。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NineRound : MonoBehaviour
    {
        Image img;
        float radius = 16;

        public void Set(Image image, float r)
        {
            img = image;
            radius = r;
            Apply();
        }

        void OnEnable() { Apply(); }
        void OnRectTransformDimensionsChange() { Apply(); }

        void Apply()
        {
            if (img == null) img = GetComponent<Image>();
            if (img == null) return;
            var size = ((RectTransform)transform).rect.size;
            float shortSide = Mathf.Min(size.x, size.y);
            float r = radius;
            // 角の部分(境界)は半径より少し大きいので、その分も含めて短辺の半分に収める
            if (shortSide > 0.5f) r = Mathf.Min(r, shortSide * 0.5f * NineUi.TexRadiusPublic / NineUi.TexBorder);
            float m = NineUi.TexRadiusPublic / Mathf.Max(0.5f, r);
            if (!Mathf.Approximately(img.pixelsPerUnitMultiplier, m)) img.pixelsPerUnitMultiplier = m;
        }
    }

    /// <summary>uGUI の部品をコードで組み立てる小さなヘルパー。</summary>
    public static class NineUi
    {
        public static TMP_FontAsset Font;

        // ================= 生成スプライト =================
        // 角丸は 96px のテクスチャ(半径32px)を9スライスで使い、pixelsPerUnitMultiplier で半径を変える。
        const int TexSize = 96;
        const int TexRadius = 32;
        static Sprite rounded, ring, ringThick, gradient, softGlow;

        public static Sprite Rounded { get { if (rounded == null) rounded = MakeRounded(0); return rounded; } }
        public static Sprite RingSprite { get { if (ring == null) ring = MakeRounded(3f); return ring; } }
        public static Sprite RingThick { get { if (ringThick == null) ringThick = MakeRounded(6f); return ringThick; } }

        static Sprite MakeRounded(float thickness)
        {
            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[TexSize * TexSize];
            for (int y = 0; y < TexSize; y++)
            for (int x = 0; x < TexSize; x++)
            {
                float a = Coverage(x + 0.5f, y + 0.5f, TexRadius);
                if (thickness > 0) a = Mathf.Clamp01(a - InnerCoverage(x + 0.5f, y + 0.5f, TexRadius, thickness));
                px[y * TexSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            float b = TexRadius + 2;
            return Sprite.Create(tex, new Rect(0, 0, TexSize, TexSize), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        // 角丸四角形の内側にある割合(端は1px幅でぼかす)
        static float Coverage(float x, float y, float r)
        {
            float s = TexSize;
            float cx = Mathf.Clamp(x, r, s - r);
            float cy = Mathf.Clamp(y, r, s - r);
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
            return Mathf.Clamp01(r - d + 0.5f);
        }

        static float InnerCoverage(float x, float y, float r, float t)
        {
            float s = TexSize;
            float ir = Mathf.Max(0.5f, r - t);
            float cx = Mathf.Clamp(x, t + ir, s - t - ir);
            float cy = Mathf.Clamp(y, t + ir, s - t - ir);
            if (x < t || y < t || x > s - t || y > s - t) return 0;
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
            return Mathf.Clamp01(ir - d + 0.5f);
        }

        /// <summary>上から下へのグラデーション(背景用)</summary>
        public static Sprite Gradient
        {
            get
            {
                if (gradient != null) return gradient;
                var tex = new Texture2D(1, 128, TextureFormat.RGBA32, false);
                tex.wrapMode = TextureWrapMode.Clamp;
                for (int y = 0; y < 128; y++)
                {
                    float t = y / 127f;
                    tex.SetPixel(0, y, Color.Lerp(NineTheme.Background, NineTheme.BackgroundTop, t * t));
                }
                tex.Apply();
                gradient = Sprite.Create(tex, new Rect(0, 0, 1, 128), new Vector2(0.5f, 0.5f), 100);
                return gradient;
            }
        }

        /// <summary>中心が明るく外へ消える丸(勝者の光など)</summary>
        public static Sprite SoftGlow
        {
            get
            {
                if (softGlow != null) return softGlow;
                int n = 128;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
                tex.wrapMode = TextureWrapMode.Clamp;
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01(1 - d);
                    tex.SetPixel(x, y, new Color(1, 1, 1, a * a));
                }
                tex.Apply();
                softGlow = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100);
                return softGlow;
            }
        }

        /// <summary>
        /// 角丸の半径(キャンバス単位)を設定する。実際の半径は要素の短辺の半分までに抑える(NineRound)。
        /// 抑えないと、9スライスの角が要素より大きくなったときに左右から押し潰され、端が細長く尖った楕円になる。
        /// </summary>
        public static void Radius(Image img, float radius)
        {
            img.type = Image.Type.Sliced;
            var r = img.GetComponent<NineRound>();
            if (r == null) r = img.gameObject.AddComponent<NineRound>();
            r.Set(img, radius);
        }

        internal const float TexRadiusPublic = TexRadius;
        internal const float TexBorder = TexRadius + 2;

        // ================= RectTransform =================
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Stretch(RectTransform rt, float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>親の左上を原点に、x/y(下向き)と幅・高さで置く。</summary>
        public static RectTransform TL(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        /// <summary>親の中心を原点に置く。</summary>
        public static RectTransform Center(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        // ================= 基本部品 =================
        public static Image Panel(string name, Transform parent, Color color, float radius = 16, bool raycast = false)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Rounded;
            img.color = color;
            img.raycastTarget = raycast;
            Radius(img, radius);
            return img;
        }

        public static Image Flat(string name, Transform parent, Color color, bool raycast = false)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        /// <summary>枠線(角丸のリング)。親いっぱいに広げる。</summary>
        public static Image Outline(Transform parent, Color color, float radius = 16, bool thick = false)
        {
            var img = Panel("Ring", parent, color, radius);
            img.sprite = thick ? RingThick : RingSprite;
            Radius(img, radius);
            Stretch(img.rectTransform);
            return img;
        }

        public static TextMeshProUGUI Text(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool bold = false)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (Font != null) t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            // 縦に収まらないと Ellipsis/Truncate は1文字も出さない(Noto Sans JP は行の高さが大きい)ので、既定ははみ出し可にする
            t.overflowMode = TextOverflowModes.Overflow;
            t.richText = true;
            return t;
        }

        public static void Fit(TextMeshProUGUI t, float max, float min = 8)
        {
            t.enableAutoSizing = true;
            t.fontSizeMin = min;
            t.fontSizeMax = max;
        }

        /// <summary>丸い札(状態表示・タグ)。</summary>
        public static TextMeshProUGUI Chip(string name, Transform parent, string text, Color color, float size = NineTheme.SizeSmall, bool filled = false)
        {
            var bg = Panel(name, parent, filled ? color : NineTheme.A(color, 0.16f), 999);
            var t = Text("Label", bg.transform, text, size, filled ? NineTheme.CardInk : color, TextAlignmentOptions.Center, true);
            Stretch(t.rectTransform, 10, 0, 10, 0);
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        public static void SetChip(TextMeshProUGUI chipLabel, string text, Color color, bool filled = false)
        {
            chipLabel.text = text;
            chipLabel.color = filled ? NineTheme.CardInk : color;
            var bg = chipLabel.transform.parent.GetComponent<Image>();
            if (bg != null) bg.color = filled ? color : NineTheme.A(color, 0.16f);
        }

        // ================= ボタン =================
        public static NineButton Button(string name, Transform parent, string label, Action onClick,
            BtnStyle style = BtnStyle.Secondary, float fontSize = NineTheme.SizeBody, float radius = 14)
        {
            var face = Panel(name, parent, Color.white, radius, raycast: true);
            var ringImg = Outline(face.transform, Color.white, radius);
            var text = Text("Label", face.transform, label, fontSize, Color.white, TextAlignmentOptions.Center, true);
            Stretch(text.rectTransform, 14, 2, 14, 2);
            Fit(text, fontSize, 10);

            var b = face.gameObject.AddComponent<Button>();
            b.targetGraphic = face;
            var colors = b.colors;
            // 倍率1.25 × 0.8 = 1 で通常時は元の色。ホバーで少し明るく、押すと暗く
            colors.normalColor = new Color(0.8f, 0.8f, 0.8f);
            colors.selectedColor = colors.normalColor;
            colors.highlightedColor = new Color(0.92f, 0.92f, 0.92f);
            colors.pressedColor = new Color(0.62f, 0.62f, 0.62f);
            colors.disabledColor = colors.normalColor; // 押せない見た目は NineButton がまとめて薄くする
            colors.colorMultiplier = 1.25f;
            colors.fadeDuration = 0.08f;
            b.colors = colors;
            if (onClick != null) b.onClick.AddListener(() => onClick());

            var nb = face.gameObject.AddComponent<NineButton>();
            nb.Init(b, face, ringImg, text);
            ApplyStyle(nb, style);
            return nb;
        }

        public static void ApplyStyle(NineButton b, BtnStyle style)
        {
            b.Style = style;
            Color face, ink, line;
            switch (style)
            {
                case BtnStyle.Primary: face = NineTheme.Accent; ink = Color.white; line = NineTheme.A(Color.white, 0f); break;
                case BtnStyle.Go: face = NineTheme.Go; ink = NineTheme.CardInk; line = NineTheme.A(Color.white, 0f); break;
                case BtnStyle.Ghost: face = NineTheme.A(NineTheme.Raised, 0f); ink = NineTheme.Muted; line = NineTheme.Line; break;
                case BtnStyle.Danger: face = NineTheme.A(NineTheme.Danger, 0.08f); ink = NineTheme.Danger; line = NineTheme.A(NineTheme.Danger, 0.55f); break;
                case BtnStyle.Quiet: face = NineTheme.Raised; ink = NineTheme.Text; line = NineTheme.A(Color.white, 0f); break;
                default: face = NineTheme.Raised; ink = NineTheme.Text; line = NineTheme.LineStrong; break;
            }
            b.Face.color = face;
            if (b.Ring != null) b.Ring.color = line;
            if (b.Label != null) b.Label.color = ink;
        }

        // ================= 入力 =================
        public static TMP_InputField Input(string name, Transform parent, string placeholder, float fontSize = NineTheme.SizeBody)
        {
            var bg = Panel(name, parent, NineTheme.Background, 12, raycast: true);
            Outline(bg.transform, NineTheme.LineStrong, 12);
            var area = Stretch(Rect("TextArea", bg.transform), 18, 4, 18, 4);
            area.gameObject.AddComponent<RectMask2D>();
            var ph = Text("Placeholder", area, placeholder, fontSize, NineTheme.Faint);
            Stretch(ph.rectTransform);
            var txt = Text("Text", area, "", fontSize, NineTheme.Text);
            Stretch(txt.rectTransform);
            txt.overflowMode = TextOverflowModes.Overflow;
            var input = bg.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = area;
            input.textComponent = txt;
            input.placeholder = ph;
            input.targetGraphic = bg;
            input.caretColor = NineTheme.Accent;
            input.customCaretColor = true;
            input.caretWidth = 3;
            input.selectionColor = NineTheme.A(NineTheme.Accent, 0.35f);
            input.fontAsset = Font;
            input.pointSize = fontSize;
            input.lineType = TMP_InputField.LineType.SingleLine;
            return input;
        }

        // ================= スライダー =================
        public static Slider Slider(string name, Transform parent, Color fillColor)
        {
            var root = Rect(name, parent);
            var hit = root.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            var track = Panel("Track", root, NineTheme.Background, 999);
            track.rectTransform.anchorMin = new Vector2(0, 0.5f);
            track.rectTransform.anchorMax = new Vector2(1, 0.5f);
            track.rectTransform.sizeDelta = new Vector2(0, 12);
            var fillArea = Rect("FillArea", root);
            fillArea.anchorMin = new Vector2(0, 0.5f);
            fillArea.anchorMax = new Vector2(1, 0.5f);
            fillArea.sizeDelta = new Vector2(0, 12);
            var fill = Panel("Fill", fillArea, fillColor, 999);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(0, 1);
            fill.rectTransform.sizeDelta = Vector2.zero;
            var handleArea = Stretch(Rect("HandleArea", root), 14, 0, 14, 0);
            var handle = Panel("Handle", handleArea, NineTheme.Text, 999);
            handle.rectTransform.sizeDelta = new Vector2(30, -14);   // 高さは親(スライダー)に合わせて伸びるので、差分で丸にする
            var s = root.gameObject.AddComponent<Slider>();
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            return s;
        }

        // ================= モーダル =================
        /// <summary>全画面の覆いと、中央のカード。戻り値は(覆い, カード)。カードの大きさは呼び出し側で決める。</summary>
        public static RectTransform Modal(string name, Transform parent, out RectTransform card, Action onOutside)
        {
            var dim = Flat(name, parent, NineTheme.Scrim, raycast: true);
            Stretch(dim.rectTransform);
            var c = Panel("Card", dim.transform, NineTheme.Surface, 22, raycast: true);
            Outline(c.transform, NineTheme.Line, 22);
            card = c.rectTransform;
            var bd = dim.gameObject.AddComponent<NineBackdrop>();
            bd.Card = card;
            bd.OnOutside = onOutside;
            dim.gameObject.SetActive(false);
            return dim.rectTransform;
        }

        // ================= スクロール =================
        public static RectTransform ScrollArea(string name, Transform parent, out ScrollRect scroll, float spacing = 8)
        {
            var root = Rect(name, parent);
            scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;
            var viewport = Stretch(Rect("Viewport", root));
            viewport.gameObject.AddComponent<RectMask2D>();
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            var content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            return content;
        }

        public static LayoutElement Height(Component c, float h)
        {
            var le = c.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = h; le.minHeight = h; le.flexibleHeight = 0;
            return le;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var c = t.GetChild(i);
                c.SetParent(null, false);
                UnityEngine.Object.Destroy(c.gameObject);
            }
        }

        public static void Show(Component c, bool on)
        {
            if (c != null && c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }

        public static void Show(GameObject g, bool on)
        {
            if (g != null && g.activeSelf != on) g.SetActive(on);
        }
    }
}
