using UnityEngine;

/// <summary>
/// 2.5D 排序帶。地板 -100 → 影子與地面特效 -80 → 後牆／側牆 -60 → 站立物件（依著地點 y，越北越後）
/// → 回饋特效 186 → 前矮牆 200。站立物件只要用 <see cref="OrderFor"/> 就會前後遮擋正確。
/// </summary>
public static class Depth25D
{
    public const int FloorDecalOrder = -80;
    public const int BackMostStandingOrder = 10;
    public const int FrontMostStandingOrder = 180;
    public const int EffectOrder = 186;

    private const float RoomMarginSouth = 3f;
    private const float RoomMarginNorth = 1f;
    private static readonly Rect FallbackRoom = new Rect(-62f, -32f, 132f, 60f);

    public static int OrderFor(float groundY)
    {
        Rect room = WorldBounds2D.Instance != null ? WorldBounds2D.Instance.GetWorldRect() : FallbackRoom;
        float t = Mathf.InverseLerp(room.yMin - RoomMarginSouth, room.yMax + RoomMarginNorth, groundY);
        return Mathf.RoundToInt(Mathf.Lerp(FrontMostStandingOrder, BackMostStandingOrder, t));
    }
}
