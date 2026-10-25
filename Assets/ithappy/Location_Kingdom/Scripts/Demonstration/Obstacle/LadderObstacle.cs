using System;
using UnityEngine;

namespace ithappy
{
    [Serializable]
    public class LadderObstacleInfo : ObstacleInfo
    {
        [SerializeField] private Transform _startPoint;
        [SerializeField] private Transform _startLadderPoint;
        [SerializeField] private Transform _middlePoint;
        [SerializeField] private Transform _endPoint;

        public Transform StartPoint => _startPoint;
        public Transform StartLadderPoint => _startLadderPoint;
        public Transform MiddlePoint => _middlePoint;
        public Transform EndPoint => _endPoint;
    }
    
    public class LadderObstacle : ObstacleBase
    {
        [SerializeField] private LadderObstacleInfo _ladderObstacleInfo;

        public override Transform StartPoint => _ladderObstacleInfo.StartLadderPoint;
        public override Transform EndPoint => _ladderObstacleInfo.StartPoint;
        public override ObstacleInfo ObstacleInfo => _ladderObstacleInfo;
        public override ObstacleType ObstacleType => ObstacleType.Ladder;
    }
}
