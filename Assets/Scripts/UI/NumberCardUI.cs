using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NumberCardUI : MonoBehaviour
{
    public bool isUsed = false;
    private int number;
    private Image background;
    private TMP_Text numberText;

    public void Start()
    {
        background = GetComponentInChildren<Image>();
        numberText = GetComponentInChildren<TMP_Text>();
    }

    public void Setup(int number)
    {
        this.number = number;
        numberText.text = number.ToString();
    }

    public void SetUsed(bool isUsed)
    {
        this.isUsed = isUsed;
        if (isUsed)
        {
            background.color = new Color(63,63,63,0);
            numberText.color = Color.white;
        }
        else
        {
            background.color = Color.white;
            numberText.color = Color.white;
        }
    }
}
