using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;

namespace GlimmerDiary.Flora
{
    ///<summary>
    /// 该版本是使用建造好的树木prefabs进行散播的算法，不涉及树木生成
    /// 地形感知散步：只对地形碰撞体投射，坡度/水线/河道/高度带过滤，
    public class TreeSystemPrefab : MonoBehaviour
    {
        [Header("Terrin wiring&alignment")]
        public TerrainGenerator terrain;
        public MeshCollider terrainCollider;
        public float waterY = 0f;

        // 树木prefabs 暂定三个部分 平原 左侧低地 高原
        [Header("Tree near the water")]
        public List<GameObject> TreeNearWater = new();
        [Header("Tree in the middle")]
        public List<GameObject> TreeInTheMiddle = new();
        [Header("Tree on the right(moutain)")]
        public List<GameObject> TreeOnClif = new();

        [Header("Distribution(Density)")]
        public int seed = 42;
        public int maxTufts = 8000;
        public float densityLowland = 0.8f;
        public float densityPlains = 1.2f;
        public float densityHighland = 0.3f;
        [Tooltip("slope, over -> not growing")]
        public float maxSlopeDeg = 15f;
        [Tooltip("safe distance from water")]
        public float waterMargin = 1f;

        [Header("Wind")]
        public Vector2 Wind = new();

        [Header("Emotion Response")]
        [Range(-1f, 1f)] public float currentValence = 0.5f;
        [Range(-1f, 1f)] public float currentArousal = 0.3f;

    }


}