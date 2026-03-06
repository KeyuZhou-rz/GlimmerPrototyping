using UnityEngine;
using System;
using System.Collections.Generic;

namespace GlimmerDiary.Flora
{
    /// <summary>
    /// Defines a complete plant type - from L-System rules to visual properties.
    /// Create different presets for Baobab, Acacia, Shrubs, etc.
    /// </summary>
    [CreateAssetMenu(fileName = "NewPlant", menuName = "GlimmerDiary/Flora/Plant Definition")]
    public class PlantDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string plantName = "Unnamed Plant";
        public PlantCategory category = PlantCategory.Tree;
        public Sprite previewIcon;
        
        [Header("L-System Configuration")]
        public LSystemPreset lSystemPreset;
        
        [Header("Trunk/Stem Appearance")]
        public TrunkSettings trunk;
        
        [Header("Foliage")]
        public FoliageSettings foliage;
        
        [Header("Animation")]
        public WindResponse windResponse;
        
        [Header("Growth")]
        public GrowthSettings growth;
        
        [Header("Emotion Affinity")]
        [Tooltip("How much this plant 'likes' positive valence (1 = thrives when happy)")]
        [Range(-1f, 1f)] public float valenceAffinity = 0.5f;
        [Tooltip("How much this plant 'likes' high arousal (1 = thrives in excitement)")]
        [Range(0f, 1f)] public float arousalAffinity = 0.3f;
    }
    
    public enum PlantCategory
    {
        Tree,           // Large, single trunk, complex branching
        Shrub,          // Multi-stem, bushy
        Grass,          // Blade-based, GPU instanced
        Flower,         // Stem + bloom
        Succulent,      // Thick, minimal branching
        Vine            // Climbing/trailing
    }
    
    [Serializable]
    public class LSystemPreset
    {
        [Header("Core Rules")]
        public string axiom = "X";
        
        [Tooltip("Production rules in format: 'Symbol|Replacement' e.g. 'X|F+[[X]-X]-F[-FX]+X'")]
        public List<LSystemRuleEntry> rules = new();
        
        [Header("Iteration")]
        [Range(1, 7)] public int baseIterations = 4;
        [Range(1, 7)] public int minIterations = 2;
        [Range(1, 7)] public int maxIterations = 6;
        
        [Header("Geometry")]
        [Range(5f, 90f)] public float baseAngle = 25f;
        [Range(0f, 30f)] public float angleVariation = 5f;
        
        [Range(0.05f, 3f)] public float baseStepLength = 0.5f;
        [Range(0.5f, 1f)] public float lengthDecayPerIteration = 0.9f;
        
        [Header("3D Rotation")]
        public bool use3DRotation = true;
        [Range(0f, 180f)] public float yawAngle = 137.5f;  // Golden angle for natural spread
        [Range(0f, 90f)] public float pitchVariation = 15f;
        
        [Header("Stochastic Options")]
        public bool useStochasticRules = false;
        public List<StochasticRuleEntry> stochasticRules = new();
    }
    
    [Serializable]
    public class LSystemRuleEntry
    {
        public char symbol = 'X';
        public string replacement = "F+[[X]-X]-F[-FX]+X";
    }
    
    [Serializable]
    public class StochasticRuleEntry
    {
        public char symbol = 'X';
        public List<WeightedRule> options = new();
    }
    
    [Serializable]
    public class WeightedRule
    {
        public string replacement;
        [Range(0f, 1f)] public float weight = 1f;
    }
    
[Serializable]
public class TrunkSettings
{
    [Header("Dimensions")]
    [Range(0.01f, 2f)] public float baseRadius = 0.15f;
    [Range(0.3f, 0.95f)] public float radiusDecay = 0.75f;
    [Range(3, 12)] public int radialSegments = 8;
    
    [Header("Width Curve (Baobab Support)")]
    [Tooltip("Enable to use AnimationCurve for trunk width instead of linear decay")]
    public bool useThicknessCurve = false;
    [Tooltip("X = normalized distance from root (0-1), Y = radius multiplier.")]
    public AnimationCurve thicknessCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.1f);
    
    [Header("Tropism (Acacia/Willow Support)")]
    [Tooltip("Enable tropism - gradual bending toward a direction")]
    public bool useTropism = false;
    [Tooltip("Direction to bend toward. UP = Acacia spread, DOWN = Weeping Willow")]
    public Vector3 tropismDirection = Vector3.up;
    [Tooltip("Strength of bending per segment.")]
    [Range(0f, 0.5f)] public float tropismStrength = 0.1f;
    
    [Header("Shape Modifiers")]
    [Tooltip("Makes trunk bulge at base (like baobab)")]
    [Range(0f, 3f)] public float baseBulge = 0f;
    [Tooltip("Twist along trunk length")]
    [Range(0f, 90f)] public float twistPerSegment = 0f;
    [Tooltip("Noise displacement for organic feel")]
    [Range(0f, 0.5f)] public float surfaceNoise = 0.05f;
    
    [Header("Materials")]
    public Material barkMaterial;
    public Color barkTintBase = new Color(0.4f, 0.3f, 0.2f);
    public Color barkTintVariation = new Color(0.1f, 0.1f, 0.05f);
    
    [Header("Texture")]
    public Texture2D barkAlbedo;
    public Texture2D barkNormal;
    [Range(0.1f, 10f)] public float barkTiling = 2f;
}
    [Serializable]
    public class FoliageSettings
    {
        [Header("Leaf Type")]
        public LeafShape leafShape = LeafShape.Oval;
        public LeafDistribution distribution = LeafDistribution.BranchTips;
        
        [Header("Leaf Dimensions")]
        public Vector2 leafSizeRange = new Vector2(0.1f, 0.3f);
        [Range(0f, 1f)] public float leafDensity = 0.7f;
        
        [Header("Leaf Clusters")]
        [Range(1, 20)] public int leavesPerCluster = 5;
        [Range(0f, 1f)] public float clusterSpread = 0.3f;
        
        [Header("Colors")]
        public Gradient leafColorGradient;
        public Color leafColorBase = new Color(0.2f, 0.5f, 0.1f);
        public Color leafColorTip = new Color(0.3f, 0.6f, 0.15f);
        [Range(0f, 1f)] public float seasonalVariation = 0.3f;
        
        [Header("Materials")]
        public Material leafMaterial;
        public Texture2D leafTexture;
        public Texture2D leafNormal;
        
        [Header("Rendering")]
        public bool useSubsurfaceScattering = true;
        [Range(0f, 1f)] public float translucency = 0.4f;
    }
    
    public enum LeafShape
    {
        Oval,           // Standard broad leaf
        Pointed,        // Like acacia
        Round,          // Like aspen
        Needle,         // Conifer
        Fan,            // Palm-like
        Compound,       // Multiple leaflets
        Heart,          // Decorative
        None            // For bare trees
    }
    
    public enum LeafDistribution
    {
        BranchTips,     // Only at end of branches
        AlongBranches,  // Distributed along branch length
        Clusters,       // Grouped clusters
        Umbrella,       // Flat canopy (acacia style)
        Layered,        // Horizontal layers (conifer style)
        Weeping,
        None
    }
    
    [Serializable]
    public class WindResponse
    {
        [Header("Trunk Sway")]
        [Range(0f, 1f)] public float trunkSwayAmount = 0.02f;
        [Range(0.1f, 2f)] public float trunkSwaySpeed = 0.5f;
        
        [Header("Branch Flex")]
        [Range(0f, 1f)] public float branchFlexAmount = 0.15f;
        [Range(0.5f, 3f)] public float branchFlexSpeed = 1.2f;
        
        [Header("Leaf Flutter")]
        [Range(0f, 1f)] public float leafFlutterAmount = 0.3f;
        [Range(1f, 5f)] public float leafFlutterSpeed = 2.5f;
        
        [Header("Physics")]
        [Range(0f, 1f)] public float windResistance = 0.5f;
        [Range(0f, 2f)] public float massSimulation = 1f;
    }
    
    [Serializable]
    public class GrowthSettings
    {
        [Header("Timing")]
        [Range(0.5f, 10f)] public float fullGrowthDuration = 3f;
        public AnimationCurve growthCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        
        [Header("Sequence")]
        public GrowthSequence sequence = GrowthSequence.RootToTip;
        [Range(0f, 1f)] public float foliageDelay = 0.6f;  // When leaves start appearing
        
        [Header("Effects")]
        public bool spawnParticlesOnGrowth = true;
        public ParticleSystem growthParticlePrefab;
    }
    
    public enum GrowthSequence
    {
        RootToTip,      // Grows from bottom up
        Simultaneous,   // All at once
        BurstFromCenter,// Expands outward
        Random          // Unpredictable emergence
    }
}
