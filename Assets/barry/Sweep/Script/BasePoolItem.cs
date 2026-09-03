using UnityEngine;

public abstract class BasePoolItem : MonoBehaviour
{
    public Vector3 initialScale;

    private bool _initialScaleCached;

    protected virtual void OnEnable()
    {
        CacheInitialScaleIfNeeded(false);
    }

    private void CacheInitialScaleIfNeeded(bool allowFallback)
    {
        if (_initialScaleCached) return;

        // 舊 Prefab 沒有填 initialScale；第一次啟用時以實際尺寸補正。
        if (initialScale == Vector3.zero)
        {
            Vector3 liveScale = transform.localScale;
            if (liveScale != Vector3.zero)
            {
                initialScale = liveScale;
            }
            else if (allowFallback)
            {
                // 已被舊流程縮成 0 時已無法反推原值；至少恢復可見，避免永久隱形。
                initialScale = Vector3.one;
            }
        }

        _initialScaleCached = initialScale != Vector3.zero;
    }

    public virtual void ResetState()
    {
        CacheInitialScaleIfNeeded(true);
        transform.localScale = initialScale;
    }

}
