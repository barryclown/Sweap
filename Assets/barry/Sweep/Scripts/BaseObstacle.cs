public abstract class BaseObstacle : BasePoolItem
{


    public ObstacleType obstacleType;


    protected virtual void Deactivate()
    {
        // 歸還自己
        ObstaclePool.Instance.ReturnObstacle(this);
    }
}