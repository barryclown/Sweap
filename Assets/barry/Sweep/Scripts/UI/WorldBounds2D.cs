using System.Collections.Generic;
using UnityEngine;

public class WorldBounds2D : MonoBehaviour
{
    public enum BoundsType
    {
        Containment, // 世界邊界 (限制在內，出不去)
        Obstacle     // 障礙物 (排斥在外，進不來)
    }

    public static WorldBounds2D Instance { get; private set; }

    // [重點註釋] 靜態列表管理所有邊界與障礙物，支援場景多個障礙物同時運作
    public static readonly List<WorldBounds2D> ActiveBounds = new List<WorldBounds2D>(16);

    [Header("核心設定")]
    [Tooltip("是否為主要的世界邊界？勾選才會被註冊為 Singleton 供 LevelSpawner 等呼叫")]
    public bool isMainWorldBoundary = true;
    public BoundsType boundsType = BoundsType.Containment;

    [Header("範圍設定 (基於 Transform 動態計算)")]
    public Vector2 centerOffset = Vector2.zero;
    public Vector2 size = new Vector2(20f, 10f);

    public Vector2 Min => (Vector2)transform.position + centerOffset - size * 0.5f;
    public Vector2 Max => (Vector2)transform.position + centerOffset + size * 0.5f;

    private void Awake()
    {
        if (isMainWorldBoundary)
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }
    }

    private void OnEnable()
    {
        if (!ActiveBounds.Contains(this))
        {
            ActiveBounds.Add(this);
        }
    }

    private void OnDisable()
    {
        ActiveBounds.Remove(this);
    }

    public Vector2 GetCenter() => (Vector2)transform.position + centerOffset;

    public Rect GetWorldRect()
    {
        Vector2 min = Min;
        Vector2 max = Max;
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    public bool IsOutside(Vector2 pos, float padding = 0f)
    {
        Vector2 min = Min;
        Vector2 max = Max;

        if (boundsType == BoundsType.Containment)
        {
            return pos.x < min.x + padding || pos.x > max.x - padding ||
                   pos.y < min.y + padding || pos.y > max.y - padding;
        }

        return pos.x > min.x - padding && pos.x < max.x + padding &&
               pos.y > min.y - padding && pos.y < max.y + padding;
    }

    public bool ConstrainToBounds(ref Vector2 pos, ref Vector2 velocity, float padding = 0f)
    {
        Vector2 min = Min;
        Vector2 max = Max;

        float minX = boundsType == BoundsType.Containment ? min.x + padding : min.x - padding;
        float maxX = boundsType == BoundsType.Containment ? max.x - padding : max.x + padding;
        float minY = boundsType == BoundsType.Containment ? min.y + padding : min.y - padding;
        float maxY = boundsType == BoundsType.Containment ? max.y - padding : max.y + padding;

        bool hit = false;

        if (boundsType == BoundsType.Containment)
        {
            if (pos.x < minX) { pos.x = minX; if (velocity.x < 0f) velocity.x = 0f; hit = true; }
            else if (pos.x > maxX) { pos.x = maxX; if (velocity.x > 0f) velocity.x = 0f; hit = true; }

            if (pos.y < minY) { pos.y = minY; if (velocity.y < 0f) velocity.y = 0f; hit = true; }
            else if (pos.y > maxY) { pos.y = maxY; if (velocity.y > 0f) velocity.y = 0f; hit = true; }
        }
        else
        {
            if (pos.x >= minX && pos.x <= maxX && pos.y >= minY && pos.y <= maxY)
            {
                hit = true;
                float dL = pos.x - minX;
                float dR = maxX - pos.x;
                float dB = pos.y - minY;
                float dT = maxY - pos.y;

                float closest = Mathf.Min(dL, dR, dB, dT);

                if (closest == dL) { pos.x = minX; if (velocity.x > 0f) velocity.x = 0f; }
                else if (closest == dR) { pos.x = maxX; if (velocity.x < 0f) velocity.x = 0f; }
                else if (closest == dB) { pos.y = minY; if (velocity.y > 0f) velocity.y = 0f; }
                else { pos.y = maxY; if (velocity.y < 0f) velocity.y = 0f; }
            }
        }

        return hit;
    }

    public void Bounce(ref Vector2 pos, ref Vector2 velocity, float padding = 0f, float bounciness = 1f)
    {
        Vector2 min = Min;
        Vector2 max = Max;

        float minX = boundsType == BoundsType.Containment ? min.x + padding : min.x - padding;
        float maxX = boundsType == BoundsType.Containment ? max.x - padding : max.x + padding;
        float minY = boundsType == BoundsType.Containment ? min.y + padding : min.y - padding;
        float maxY = boundsType == BoundsType.Containment ? max.y - padding : max.y + padding;

        if (boundsType == BoundsType.Containment)
        {
            if (pos.x < minX) { pos.x = minX; if (velocity.x < 0f) velocity.x = -velocity.x * bounciness; }
            else if (pos.x > maxX) { pos.x = maxX; if (velocity.x > 0f) velocity.x = -velocity.x * bounciness; }

            if (pos.y < minY) { pos.y = minY; if (velocity.y < 0f) velocity.y = -velocity.y * bounciness; }
            else if (pos.y > maxY) { pos.y = maxY; if (velocity.y > 0f) velocity.y = -velocity.y * bounciness; }
        }
        else
        {
            if (pos.x >= minX && pos.x <= maxX && pos.y >= minY && pos.y <= maxY)
            {
                float dL = pos.x - minX;
                float dR = maxX - pos.x;
                float dB = pos.y - minY;
                float dT = maxY - pos.y;

                float closest = Mathf.Min(dL, dR, dB, dT);

                if (closest == dL) { pos.x = minX; if (velocity.x > 0f) velocity.x = -velocity.x * bounciness; }
                else if (closest == dR) { pos.x = maxX; if (velocity.x < 0f) velocity.x = -velocity.x * bounciness; }
                else if (closest == dB) { pos.y = minY; if (velocity.y > 0f) velocity.y = -velocity.y * bounciness; }
                else { pos.y = maxY; if (velocity.y < 0f) velocity.y = -velocity.y * bounciness; }
            }
        }
    }

    public bool TryGetHitPointAndNormalWorld(Vector2 pos, out Vector2 hitPoint, out Vector2 normal, float padding = 0f)
    {
        hitPoint = pos;
        normal = Vector2.up;

        Vector2 min = Min;
        Vector2 max = Max;

        float minX = boundsType == BoundsType.Containment ? min.x + padding : min.x - padding;
        float maxX = boundsType == BoundsType.Containment ? max.x - padding : max.x + padding;
        float minY = boundsType == BoundsType.Containment ? min.y + padding : min.y - padding;
        float maxY = boundsType == BoundsType.Containment ? max.y - padding : max.y + padding;

        if (boundsType == BoundsType.Containment)
        {
            if (pos.x >= minX && pos.x <= maxX && pos.y >= minY && pos.y <= maxY)
                return false;

            hitPoint.x = Mathf.Clamp(pos.x, minX, maxX);
            hitPoint.y = Mathf.Clamp(pos.y, minY, maxY);

            Vector2 inward = hitPoint - pos;

            if (inward.sqrMagnitude > 1e-6f)
            {
                normal = inward.normalized;
            }
            else
            {
                float dL = pos.x - minX;
                float dR = maxX - pos.x;
                float dB = pos.y - minY;
                float dT = maxY - pos.y;
                float closest = Mathf.Min(dL, dR, dB, dT);

                if (closest == dL) normal = Vector2.right;
                else if (closest == dR) normal = Vector2.left;
                else if (closest == dB) normal = Vector2.up;
                else normal = Vector2.down;
            }
            return true;
        }
        else
        {
            if (pos.x < minX || pos.x > maxX || pos.y < minY || pos.y > maxY)
                return false;

            float dL = pos.x - minX;
            float dR = maxX - pos.x;
            float dB = pos.y - minY;
            float dT = maxY - pos.y;
            float closest = Mathf.Min(dL, dR, dB, dT);

            if (closest == dL) { hitPoint.x = minX; normal = Vector2.left; }
            else if (closest == dR) { hitPoint.x = maxX; normal = Vector2.right; }
            else if (closest == dB) { hitPoint.y = minY; normal = Vector2.down; }
            else { hitPoint.y = maxY; normal = Vector2.up; }

            return true;
        }
    }

    public static void ApplyAllBounces(ref Vector2 pos, ref Vector2 velocity, float padding, float bounciness)
    {
        for (int i = 0; i < ActiveBounds.Count; i++)
        {
            var bound = ActiveBounds[i];
            if (bound.IsOutside(pos, padding))
            {
                bound.Bounce(ref pos, ref velocity, padding, bounciness);
            }
        }
    }

    public static bool TryHandlePlayerCollision(Vector2 nextPos, Vector2 moveDir, ref Vector2 safePos, ref Vector2 tempVel, out Vector2 hitPoint, out Vector2 hitNormal, float padding = 0f)
    {
        hitPoint = nextPos;
        hitNormal = -moveDir;
        bool hasHit = false;

        for (int i = 0; i < ActiveBounds.Count; i++)
        {
            var bound = ActiveBounds[i];
            if (bound.IsOutside(nextPos, padding))
            {
                hasHit = true;
                bound.Bounce(ref safePos, ref tempVel, padding, 0f);

                if (!bound.TryGetHitPointAndNormalWorld(safePos, out hitPoint, out hitNormal, padding))
                {
                    hitPoint = safePos;
                    hitNormal = -moveDir;
                }
                break;
            }
        }
        return hasHit;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Vector2 min = Min;
        Vector2 max = Max;

        Gizmos.color = boundsType == BoundsType.Containment ? Color.cyan : Color.red;

        Vector3 bl = new Vector3(min.x, min.y, 0f);
        Vector3 br = new Vector3(max.x, min.y, 0f);
        Vector3 tr = new Vector3(max.x, max.y, 0f);
        Vector3 tl = new Vector3(min.x, max.y, 0f);

        Gizmos.DrawLine(bl, br);
        Gizmos.DrawLine(br, tr);
        Gizmos.DrawLine(tr, tl);
        Gizmos.DrawLine(tl, bl);

        Gizmos.color = new Color(Gizmos.color.r, Gizmos.color.g, Gizmos.color.b, 0.2f);
        Gizmos.DrawCube(GetCenter(), size);
    }
#endif
}