using UnityEngine;

public abstract class BasePoolItem : MonoBehaviour
{
    public Vector3 initialScale;

    private bool _initialScaleCached;

    protected virtual void OnEnable()
    {
        CacheInitialScaleIfNeeded();
    }

    private void CacheInitialScaleIfNeeded()
    {
        if (_initialScaleCached) return;

        // 舊 Prefab 沒有填 initialScale；第一次啟用時以實際尺寸補正。
        if (initialScale == Vector3.zero)
        {
            initialScale = transform.localScale;
        }

        _initialScaleCached = true;
    }

    public virtual void ResetState()
    {
        CacheInitialScaleIfNeeded();
        transform.localScale = initialScale;
    }

}
