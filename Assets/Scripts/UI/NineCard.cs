using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nine.UI
{
    /// <summary>
    /// 数字カード1枚の表示。手札・使用済み一覧・場・結果・履歴で共通に使う。
    /// 見た目の状態は「明るい紙(使える)」「沈んだ面(使用済み)」「青い面(確定済み・中身は伏せる)」「点線の空き」などで、
    /// 色だけでなく明るさ・枠・文字でも区別できるようにしている。
    /// </summary>
    public sealed class NineCard : MonoBehaviour
    {
        public Image Bg, Ring, Glow, Stripe;
        public TextMeshProUGUI Num, Tag;
        public Button Button;
        float height = 100, width = 72;
        bool hasTag;

        public RectTransform Rt { get { return (RectTransform)transform; } }

        public static NineCard Create(string name, Transform parent, bool clickable = false, bool withTag = false, bool withStripe = false)
        {
            var root = NineUi.Rect(name, parent);
            var card = root.gameObject.AddComponent<NineCard>();

            card.Glow = NineUi.Flat("Glow", root, NineTheme.A(NineTheme.Gold, 0f));
            card.Glow.sprite = NineUi.SoftGlow;
            NineUi.Stretch(card.Glow.rectTransform, -40, -40, -40, -40);

            card.Bg = NineUi.Panel("Face", root, NineTheme.CardFace, 12, raycast: clickable);
            NineUi.Stretch(card.Bg.rectTransform);
            card.Ring = NineUi.Outline(card.Bg.transform, NineTheme.A(Color.white, 0f), 12);

            if (withStripe)
            {
                card.Stripe = NineUi.Panel("Stripe", card.Bg.transform, Color.white, 4);
                var st = card.Stripe.rectTransform;
                st.anchorMin = new Vector2(0.2f, 0); st.anchorMax = new Vector2(0.8f, 0);
                st.pivot = new Vector2(0.5f, 0);
                st.anchoredPosition = new Vector2(0, 6); st.sizeDelta = new Vector2(0, 6);
                card.Stripe.gameObject.SetActive(false);
            }

            card.Num = NineUi.Text("Num", card.Bg.transform, "", 40, NineTheme.CardInk, TextAlignmentOptions.Center, true);
            NineUi.Stretch(card.Num.rectTransform, 2, 2, 2, 2);
            card.Num.overflowMode = TextOverflowModes.Overflow;

            if (withTag)
            {
                card.hasTag = true;
                card.Tag = NineUi.Chip("Tag", root, "", NineTheme.Gold, NineTheme.SizeSmall, filled: true);
                var tr = (RectTransform)card.Tag.transform.parent;
                tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 1);
                tr.pivot = new Vector2(0.5f, 0.5f);
                tr.gameObject.SetActive(false);
            }

            if (clickable)
            {
                card.Button = card.Bg.gameObject.AddComponent<Button>();
                card.Button.targetGraphic = card.Bg;
                var c = card.Button.colors;
                c.normalColor = new Color(0.86f, 0.86f, 0.86f);
                c.selectedColor = c.normalColor;
                c.highlightedColor = new Color(0.95f, 0.95f, 0.95f);
                c.pressedColor = new Color(0.7f, 0.7f, 0.7f);
                c.disabledColor = c.normalColor;
                c.colorMultiplier = 1.163f;
                c.fadeDuration = 0.06f;
                card.Button.colors = c;
                card.Button.transition = Selectable.Transition.ColorTint;
                var nav = card.Button.navigation; nav.mode = Navigation.Mode.None; card.Button.navigation = nav;
            }
            return card;
        }

        /// <summary>カードの大きさに合わせて、角丸・文字・札を整える。大きさは呼び出し側が決める。</summary>
        public void Layout(float w, float h)
        {
            height = h;
            width = w;
            Rt.sizeDelta = new Vector2(w, h);
            float r = Mathf.Clamp(Mathf.Min(w, h) * 0.16f, 4, 20);
            NineUi.Radius(Bg, r);
            NineUi.Radius(Ring, r);
            FitNumber();
            if (Stripe != null)
            {
                Stripe.rectTransform.sizeDelta = new Vector2(0, Mathf.Max(3, h * 0.05f));
                Stripe.rectTransform.anchoredPosition = new Vector2(0, Mathf.Max(3, h * 0.06f));
            }
            if (hasTag)
            {
                var tr = (RectTransform)Tag.transform.parent;
                float th = Mathf.Max(18, h * 0.22f);
                tr.sizeDelta = new Vector2(Mathf.Max(w * 0.9f, th * 2.6f), th);
                tr.anchoredPosition = new Vector2(0, 0);
                Tag.fontSize = th * 0.62f;
            }
        }

        void Paint(Color bg, Color ink, Color ring, string text)
        {
            Bg.color = bg;
            Num.color = ink;
            Ring.color = ring;
            Num.text = text;
            FitNumber();
        }

        // 2桁(と符号付き)の数字でもカードの幅に収まる大きさにする
        void FitNumber()
        {
            int len = string.IsNullOrEmpty(Num.text) ? 1 : Num.text.Length;
            float byWidth = len >= 3 ? 0.34f : len == 2 ? 0.44f : 0.62f;
            Num.fontSize = Mathf.Max(8, Mathf.Min(height * 0.48f, width * byWidth));
        }

        public void SetFace(int value) { Paint(NineTheme.CardFace, NineTheme.CardInk, NineTheme.A(Color.white, 0), value.ToString()); }
        public void SetUsed(int value) { Paint(NineTheme.CardUsed, NineTheme.CardUsedInk, NineTheme.A(Color.white, 0), value.ToString()); }
        public void SetSelected(int value) { Paint(NineTheme.Accent, Color.white, NineTheme.A(Color.white, 0.9f), value.ToString()); }
        /// <summary>確定済み。text は伏せるなら「?」、観戦者には実際の数字。</summary>
        public void SetPending(string text) { Paint(NineTheme.CardPending, NineTheme.Text, NineTheme.A(NineTheme.Accent, 0.7f), text); }
        public void SetEmpty() { Paint(NineTheme.A(NineTheme.CardEmpty, 0.6f), NineTheme.Faint, NineTheme.Line, "–"); }
        public void SetPoint(int v)
        {
            var c = v >= 0 ? NineTheme.Gold : NineTheme.Danger;
            Paint(NineTheme.A(c, 0.14f), c, NineTheme.A(c, 0.9f), (v > 0 ? "+" : "") + v);
        }
        /// <summary>席の一覧の小さなカード。まだ使えるものは明るい面、使ったものは枠だけにして、手札の大きなカードと見分けやすくする。</summary>
        public void SetMini(int value, bool used)
        {
            if (used) Paint(NineTheme.A(NineTheme.Background, 0.5f), NineTheme.Faint, NineTheme.Line, value.ToString());
            else Paint(NineTheme.Hex("3A4459"), NineTheme.Text, NineTheme.A(Color.white, 0), value.ToString());
            Ring.sprite = NineUi.RingSprite;
        }

        public void SetText(string text, Color bg, Color ink) { Paint(bg, ink, NineTheme.A(Color.white, 0), text); }

        public void SetStripe(Color? c)
        {
            if (Stripe == null) return;
            Stripe.gameObject.SetActive(c.HasValue);
            if (c.HasValue) Stripe.color = c.Value;
        }

        /// <summary>勝ち(得点を取った)を、金の枠と光で示す。</summary>
        public void SetWinner(bool on)
        {
            Glow.color = NineTheme.A(NineTheme.Gold, on ? 0.38f : 0f);
            if (on) Ring.color = NineTheme.Gold;
            Ring.sprite = on ? NineUi.RingThick : NineUi.RingSprite;
        }

        public void SetTag(string text, Color color)
        {
            if (!hasTag) return;
            bool on = !string.IsNullOrEmpty(text);
            Tag.transform.parent.gameObject.SetActive(on);
            if (on) NineUi.SetChip(Tag, text, color, filled: true);
        }

        public void SetClick(Action onClick)
        {
            if (Button == null) return;
            Button.onClick.RemoveAllListeners();
            if (onClick != null) Button.onClick.AddListener(() => onClick());
        }
    }
}
