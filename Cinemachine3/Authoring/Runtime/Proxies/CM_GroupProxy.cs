using System;
using UnityEngine;
using Unity.Entities;
using System.Collections.Generic;
using Unity.Transforms;
using Unity.Cinemachine.Common;

namespace Unity.Cinemachine3.Authoring
{
    [DisallowMultipleComponent]
    [SaveDuringPlay]
    [ExecuteAlways]
    public class CM_GroupProxy : DynamicBufferProxy<CM_GroupBufferElement>
    {
        [Serializable] public struct Target
        {
            /// <summary>The target objects.  This object's position and orientation will contribute to the
            /// group's average position and orientation, in accordance with its weight</summary>
            [Tooltip("The target objects.  This object's position and orientation will contribute to"
                + "the group's average position and orientation, in accordance with its weight")]
            public Transform target;
            /// <summary>How much weight to give the target when averaging.  Cannot be negative</summary>
            [Tooltip("How much weight to give the target when averaging.  Cannot be negative")]
            public float weight;
        }

        /// <summary>This enum defines the options available for the update method.</summary>
        public enum UpdateMethod
        {
            /// <summary>Updated in normal MonoBehaviour Update.</summary>
            Update,
            /// <summary>Updated in sync with the Physics module, in FixedUpdate</summary>
            FixedUpdate,
            /// <summary>Updated in MonoBehaviour LateUpdate.</summary>
            LateUpdate
        };

        /// <summary>When to update the group's transform based on the position of the group members</summary>
        [Tooltip("When to update the group's transform based on the position of the group members")]
        public UpdateMethod m_UpdateMethod = UpdateMethod.LateUpdate;

        /// <summary>The target objects, together with their weights and radii, that will
        /// contribute to the group's average position, orientation, and size</summary>
        [NoSaveDuringPlay]
        [Tooltip("The target objects together with their weights that will contribute to "
            + "the group's average position, orientation, and size.")]
        public List<Target> m_Targets = new List<Target>();

        private void OnValidate()
        {
            for (int i = 0; i < m_Targets.Count; ++i)
            {
                var t = m_Targets[i];
                t.weight = Mathf.Max(0, t.weight);
                m_Targets[i] = t;
            }
        }

        /// <summary>Return true if there are no members with weight > 0</summary>
        public bool IsEmpty
        {
            get
            {
                var count = m_Targets.Count;
                for (int i = 0; i < count; ++i)
                    if (m_Targets[i].target != null && m_Targets[i].weight > MathHelpers.Epsilon)
                        return false;
                return true;
            }
        }

        /// <summary>Add a member to the group</summary>
        public void AddMember(Transform t, float weight)
        {
            if (m_Targets == null)
                m_Targets = new List<Target>();
            m_Targets.Add(new Target { target = t, weight = weight } );
        }

        /// <summary>Remove a member from the group</summary>
        public void RemoveMember(Transform t)
        {
            int index = FindMember(t);
            if (index >= 0)
                m_Targets.RemoveAt(index);
        }

        /// <summary>Locate a member's index in the group. Returns -1 if not a member</summary>
        public int FindMember(Transform t)
        {
            if (m_Targets != null)
            {
                var count = m_Targets.Count;
                for (int i = 0; i < count; ++i)
                    if (m_Targets[i].target == t)
                        return i;
            }
            return -1;
        }

        protected EntityManager ActiveEntityManager { get { return World.Active?.EntityManager; } }

#if true // GML todo something better here
        protected Entity EnsureTargetCompliance(Transform target)
        {
            if (target == null)
                return Entity.Null;

            var m = ActiveEntityManager;
            if (m == null)
                return Entity.Null;

            var goe = target.GetComponent<GameObjectEntity>();
            if (goe == null)
                goe = target.gameObject.AddComponent<GameObjectEntity>();

            var e = goe.Entity;
            if (e != Entity.Null)
            {
                if (!m.HasComponent<CM_Target>(e))
                    m.AddComponentData(e, new CM_Target());
                if (!m.HasComponent<LocalToWorld>(e))
                    m.AddComponentData(e, new LocalToWorld());
                if (!m.HasComponent<CopyTransformFromGameObject>(e))
                    m.AddComponentData(e, new CopyTransformFromGameObject());
            }
            return e;
        }
#endif

        List<CM_GroupBufferElement> scratchList = new List<CM_GroupBufferElement>();
        void DoUpdate()
        {
            if (IsEmpty)
                return;

            scratchList.Clear();
            var count = m_Targets.Count;
            for (int i = 0; i < count; ++i)
            {
                var t = m_Targets[i];
                var e = EnsureTargetCompliance(t.target);
                if (e != Entity.Null)
                    scratchList.Add(new CM_GroupBufferElement { target = e, weight = t.weight });
            }
            SetValue(scratchList);
        }

        void FixedUpdate()
        {
            if (m_UpdateMethod == UpdateMethod.FixedUpdate)
                DoUpdate();
        }

        void Update()
        {
            if (!Application.isPlaying || m_UpdateMethod == UpdateMethod.Update)
                DoUpdate();
        }

        void LateUpdate()
        {
            if (m_UpdateMethod == UpdateMethod.LateUpdate)
                DoUpdate();
        }
    }
}
