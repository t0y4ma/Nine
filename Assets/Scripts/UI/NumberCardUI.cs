using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class NumberCardUI : MonoBehaviour
{
    public bool isUsed = false;
    private int number;
    [SerializeField] private TMP_Text numberText;
    [SerializeField] public Button button;

    private static readonly Color ColorNormal = new Color(0.98f, 0.98f, 0.99f, 1f);
    private static readonly Color ColorUsed = new Color(0.55f, 0.58f, 0.63f, 1f);
    private static readonly Color ColorSelected = new Color(1f, 0.82f, 0.25f, 1f);
    private static readonly Color ColorPending = new Color(0.35f, 0.42f, 0.55f, 1f);
    private static readonly Color TextDark = new Color(0.15f, 0.17f, 0.22f, 1f);
    private static readonly Color TextLight = Color.white;

    public void Setup(int number)
    {
        this.number = number;
        numberText.text = number.ToString();
        button.interactable = true;
        SetBackground(ColorNormal, TextDark);
    }

    // 表示専用(クリック不可)のカードとして、任意のテキスト(数字や"?")を表示する
    public void SetupDisplay(string text)
    {
        numberText.text = text;
        button.interactable = false;
        if (text == "?") SetBackground(ColorPending, TextLight);
        else SetBackground(ColorNormal, TextDark);
    }

    public void SetUsed(bool isUsed)
    {
        this.isUsed = isUsed;
        button.interactable = !isUsed;
        SetBackground(isUsed ? ColorUsed : ColorNormal, isUsed ? TextLight : TextDark);
    }

    // 選択中(未確定)であることを示す見た目にする
    public void SetSelected(bool selected)
    {
        if (selected) SetBackground(ColorSelected, TextDark);
        else if (isUsed) SetBackground(ColorUsed, TextLight);
        else SetBackground(ColorNormal, TextDark);
    }

    private void SetBackground(Color bgColor, Color textColor)
    {
        var bg = transform.Find("Background");
        var img = bg != null ? bg.GetComponent<Image>() : null;
        if (img != null) img.color = bgColor;
        if (numberText != null) numberText.color = textColor;
    }

    public void SetListener(UnityAction listener)
    {
        button.onClick.AddListener(listener);
    }
}
