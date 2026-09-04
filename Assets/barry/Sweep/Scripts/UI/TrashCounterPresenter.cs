using UnityEngine;
using TMPro;
using UnityEngine.Events;

public class TrashCounterPresenter : MonoBehaviour
{
    [SerializeField] private TMP_Text trashCounterText;

    [System.Serializable]
    public class IntIntEvent : UnityEvent<int, int> { }

    public IntIntEvent onChanged; // 想在 Inspector 綁別的 UI 也可以

    private void OnEnable()
    {
        TrashCounter.Changed += HandleChanged;

        // 立刻同步一次，避免時序問題
        HandleChanged(TrashCounter.Collected, TrashCounter.Total);
    }

    private void OnDisable()
    {
        TrashCounter.Changed -= HandleChanged;
    }

    private void HandleChanged(int collected, int total)
    {
        if (trashCounterText != null)
            trashCounterText.text = $"{collected}/{total}";

        onChanged?.Invoke(collected, total);
    }
}
