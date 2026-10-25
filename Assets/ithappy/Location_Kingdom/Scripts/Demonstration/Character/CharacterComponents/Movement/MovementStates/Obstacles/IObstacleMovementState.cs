using System;

namespace ithappy
{
    public interface IObstacleMovementState
    {
        public void Overcome(ObstacleInfo obstacles, Action<bool> callback);
    }
}
