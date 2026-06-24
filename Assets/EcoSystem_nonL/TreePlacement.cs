using UnityEngine;
using System.Collections.Generic;
using JetBrains.Annotations;
using GlimmerDiary.Data;

namespace GlimmerDiary.Ecosystem_nonL
{
    /// <summary>
    /// The second version of ecosystem, with no Lsystem generation.
    /// Following repecting rules to place all the trees in terms of position, emotion input and time.
    /// </summary>
    
    public class EcosystemManager : MonoBehaviour
    {

        // the following define the basic variables used in ecosystem
        public Vector2 areaSize = new Vector2(160f, 160f);
        public Transform groundPlane;

        [Header("Terrain Alignment")]
        [Tooltip("Auto-found if left empty. Placement is centered on this terrain.")]
        public TerrainGenerator terrain;
        public bool matchTerrainSize = true;

        [Header("Tree Near the water")]
        public List<GameObject> TreeNearTheWater;
        
        [Header("Tree in the plain")]
        public List<GameObject> TreeInPlain;

        [Header("Tree on the clif&Mountain")]
        public List<GameObject> TreeOnClif;

        [Header("Density Settings")]
        [Range(0, 50)]public int nearTheRiver = 10;
        [Range(0, 50)]public int inThePlain = 10;
        [Range(0, 20)]public int onTheClif = 5;

        public EmotionVector emotionvector;

        // The following define basic data structure used to store and spawn trees
        private List<Vector3> _occupiedPos = new();
        private List<TreeConfigData> _TreeConfigs = new();

        public List<TreeData> Populate()
        {
            public List<TreeData> trees = new();
            for (int i = 0; i < )
        }

        private void Awake()
        {
            
        }

        private void Start()
        {
            
        }




        
    }
}

