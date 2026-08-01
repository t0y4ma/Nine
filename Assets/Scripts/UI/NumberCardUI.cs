using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class NumberCardUI : MonoBehaviour
{
    public bool isUsed = false;
    private int number;
    [SerializeField] private TMP_Text numberText;
    [SerializeField] private Button button;
    private ColorBlock colorBlock;

    public void Setup(int number)
    {
        this.number = number;
        numberText.text = number.ToString();
        colorBlock = new ColorBlock();
        colorBlock.normalColor = Color.white;
        colorBlock.disabledColor = new Color(0.5f,0.5f,0.5f,1);
        colorBlock.pressedColor = new Color(0.25f,0.25f,0.25f,1);
    }

    public void SetUsed(bool isUsed)
    {
        button.interactable = !isUsed;
        if (isUsed)
        {
            numberText.color = Color.white;
        }
        else
        {
            numberText.color = Color.black;
        }
    }

    public void SetListener(UnityAction listener)
    {
        button.onClick.AddListener(listener);
    }
}
