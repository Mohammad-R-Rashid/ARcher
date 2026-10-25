using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ithappy
{
    public class PatrolCharacter : CharacterBase
    {
        [SerializeField] private List<PatrolWayPoint> _wayPoints;
        
        protected Dictionary<Type, CharacterStateBase> _states;
        
        protected override Dictionary<Type, CharacterStateBase> States => _states;
        public List<PatrolWayPoint> GetWayPoints() => _wayPoints;

        public override void Initialize()
        {
            base.Initialize();
            
            _states = new Dictionary<Type, CharacterStateBase>
            {
                {
                    typeof(ObstacleOvercomeCharacterState), new ObstacleOvercomeCharacterState(this, _movement)
                },
                {
                    typeof(PatrolCharacterState), new PatrolCharacterState(this, _movement, _wayPoints)
                },
            };
            
            _states[typeof(PatrolCharacterState)].SetStatesToTransition(new List<CharacterStateBase>
            {
                _states[typeof(ObstacleOvercomeCharacterState)],
            });
            _states[typeof(ObstacleOvercomeCharacterState)].SetStatesToTransition(new List<CharacterStateBase>());

            TransitionToState(typeof(PatrolCharacterState));
        }
        
        private void OnDrawGizmosSelected()
        {
            if (_wayPoints == null || _wayPoints.Count == 0) return;
            
            for (int i = 0; i < _wayPoints.Count; i++)
            {
                if (_wayPoints[i] == null) continue;
                
                Gizmos.color = _wayPoints[i].ShouldRotate ? Color.magenta : Color.green;
                
                Gizmos.DrawSphere(_wayPoints[i].transform.position, 0.5f);
                
                if (i < _wayPoints.Count - 1 && _wayPoints[i + 1] != null)
                {
                    Gizmos.DrawLine(_wayPoints[i].transform.position, _wayPoints[i + 1].transform.position);
                }
                else if (_wayPoints.Count > 1 && _wayPoints[0] != null)
                {
                    Gizmos.DrawLine(_wayPoints[i].transform.position, _wayPoints[0].transform.position);
                }
                
                if (i < _wayPoints.Count - 1 && _wayPoints[i + 1] != null)
                {
                    DrawArrow(_wayPoints[i].transform.position, _wayPoints[i + 1].transform.position);
                }
                else if (_wayPoints.Count > 1 && _wayPoints[0] != null)
                {
                    DrawArrow(_wayPoints[i].transform.position, _wayPoints[0].transform.position);
                }
            }
            
            if (_wayPoints[0] != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(transform.position, _wayPoints[0].transform.position);
            }
        }

        private void DrawArrow(Vector3 from, Vector3 to)
        {
            Vector3 direction = (to - from).normalized;
            float arrowHeadLength = 0.5f;
            float arrowHeadAngle = 20.0f;

            Vector3 right = Quaternion.LookRotation(direction) * Quaternion.Euler(0, 180 + arrowHeadAngle, 0) * Vector3.forward;
            Vector3 left = Quaternion.LookRotation(direction) * Quaternion.Euler(0, 180 - arrowHeadAngle, 0) * Vector3.forward;

            Gizmos.DrawRay(to, right * arrowHeadLength);
            Gizmos.DrawRay(to, left * arrowHeadLength);
        }
        
        private void OnDrawGizmos()
        {
            if (_wayPoints == null || _wayPoints.Count == 0) return;

            Gizmos.color = new Color(0, 1, 0, 0.3f);
            
            for (int i = 0; i < _wayPoints.Count; i++)
            {
                if (_wayPoints[i] == null) continue;
                
                Gizmos.DrawSphere(_wayPoints[i].transform.position, 0.3f);
            }
        }
    }
}