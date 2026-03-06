// This file contains instructions for creating PlantDefinition presets in Unity Editor
// Since ScriptableObjects can't be created as code files, use these configurations manually

/*
================================================================================
BAOBAB TREE PRESET
================================================================================
Create: Right-click in Project > Create > GlimmerDiary > Flora > Plant Definition
Name: Baobab

Settings:

[Identity]
- Plant Name: "Baobab"
- Category: Tree

[L-System Configuration]
- Axiom: "X"
- Rules:
  - X → "FFFFF[&X][/&X][//&X][///&X]"  (Long trunk, 4 branches at 90° azimuthal intervals)
  - F → "F"  (No trunk multiplication - keeps it thick)
- Base Iterations: 3
- Min Iterations: 2
- Max Iterations: 4
- Base Angle: 35
- Angle Variation: 10
- Base Step Length: 0.8
- Length Decay: 0.85
- Use 3D Rotation: true
- Yaw Angle: 90 (90° roll step — 4 × 90° = 360° uniform distribution)
- Pitch Variation: 20

[Trunk Settings]
- Base Radius: 0.5 (THICK trunk)
- Radius Decay: 0.88
- Radial Segments: 10
- Base Bulge: 0.8 (Signature baobab bulge)
- Twist Per Segment: 5
- Surface Noise: 0.08
- Bark Tint Base: RGB(0.35, 0.28, 0.22) - Grey-brown
- Bark Tiling: 3

[Foliage]
- Leaf Shape: Round
- Distribution: Branch Tips (Baobabs have sparse canopy)
- Leaf Size Range: (0.15, 0.35)
- Leaf Density: 0.4 (sparse)
- Leaves Per Cluster: 8
- Cluster Spread: 0.5
- Leaf Color Base: RGB(0.25, 0.45, 0.15) - Deep green
- Leaf Color Tip: RGB(0.35, 0.55, 0.2)
- Use Subsurface Scattering: true
- Translucency: 0.3

[Wind Response]
- Trunk Sway Amount: 0.01 (Very stiff trunk)
- Trunk Sway Speed: 0.3
- Branch Flex Amount: 0.1
- Branch Flex Speed: 0.8
- Leaf Flutter Amount: 0.25
- Leaf Flutter Speed: 2

[Growth]
- Full Growth Duration: 5
- Sequence: Root To Tip
- Foliage Delay: 0.7

[Emotion Affinity]
- Valence Affinity: 0.3 (Moderately affected by emotion)
- Arousal Affinity: 0.2

================================================================================
ACACIA TREE PRESET  
================================================================================
Create: Right-click in Project > Create > GlimmerDiary > Flora > Plant Definition
Name: Acacia

Settings:

[Identity]
- Plant Name: "Acacia"
- Category: Tree

[L-System Configuration]
- Axiom: "X"
- Rules:
  - X → "FFF[&FX][/&FX][//&FX][///&FX][////&FX]"  (Stem + 5 branches at 72° azimuthal intervals)
  - F → "F"
- Base Iterations: 3
- Min Iterations: 3
- Max Iterations: 5
- Base Angle: 40
- Angle Variation: 8
- Base Step Length: 0.5
- Length Decay: 0.75
- Use 3D Rotation: true
- Yaw Angle: 72 (72° roll step — 5 × 72° = 360° pentagon distribution)
- Pitch Variation: 10

[Trunk Settings]
- Base Radius: 0.12
- Radius Decay: 0.78
- Radial Segments: 8
- Base Bulge: 0 (No bulge)
- Twist Per Segment: 3
- Surface Noise: 0.04
- Bark Tint Base: RGB(0.3, 0.22, 0.15) - Dark brown
- Bark Tiling: 2.5

[Foliage]
- Leaf Shape: Pointed (Acacia have small pointed leaves)
- Distribution: Umbrella (Flat canopy)
- Leaf Size Range: (0.05, 0.12) - Small leaves
- Leaf Density: 0.8 (Dense canopy)
- Leaves Per Cluster: 15
- Cluster Spread: 0.3
- Leaf Color Base: RGB(0.3, 0.5, 0.2)
- Leaf Color Tip: RGB(0.4, 0.6, 0.25)
- Use Subsurface Scattering: true
- Translucency: 0.5

[Wind Response]
- Trunk Sway Amount: 0.03
- Trunk Sway Speed: 0.5
- Branch Flex Amount: 0.2
- Branch Flex Speed: 1.2
- Leaf Flutter Amount: 0.4
- Leaf Flutter Speed: 3

[Growth]
- Full Growth Duration: 4
- Sequence: Root To Tip
- Foliage Delay: 0.5

[Emotion Affinity]
- Valence Affinity: 0.5
- Arousal Affinity: 0.4

================================================================================
SAVANNA SHRUB PRESET
================================================================================
Name: SavannaShrub

[Identity]
- Plant Name: "Savanna Shrub"
- Category: Shrub

[L-System Configuration]
- Axiom: "X"
- Rules:
  - X → "F[&FX][/&FX][//&FX][///&FX]"  (Short stem + 4 branches at 90° azimuthal intervals)
  - F → "F"
- Base Iterations: 3
- Min Iterations: 2
- Max Iterations: 4
- Base Angle: 40
- Angle Variation: 15
- Base Step Length: 0.3
- Length Decay: 0.8
- Use 3D Rotation: true
- Yaw Angle: 90 (90° roll step — 4 × 90° = 360° uniform distribution)

[Trunk Settings]
- Base Radius: 0.03
- Radius Decay: 0.85
- Radial Segments: 6
- Base Bulge: 0
- Surface Noise: 0.02
- Bark Tint Base: RGB(0.4, 0.32, 0.2)

[Foliage]
- Leaf Shape: Oval
- Distribution: Along Branches
- Leaf Size Range: (0.08, 0.15)
- Leaf Density: 0.7
- Leaves Per Cluster: 6
- Cluster Spread: 0.15
- Leaf Color Base: RGB(0.35, 0.5, 0.25)
- Leaf Color Tip: RGB(0.5, 0.65, 0.3)
- Use Subsurface Scattering: true
- Translucency: 0.4

[Wind Response]
- Trunk Sway Amount: 0.08
- Branch Flex Amount: 0.25
- Leaf Flutter Amount: 0.35

[Growth]
- Full Growth Duration: 2
- Foliage Delay: 0.3

[Emotion Affinity]
- Valence Affinity: 0.6
- Arousal Affinity: 0.5

================================================================================
TALL SAVANNA GRASS PRESET (for GrassSystem)
================================================================================
Name: TallSavannaGrass

[Appearance]
- Base Color: RGB(0.65, 0.55, 0.35) - Golden brown
- Tip Color: RGB(0.85, 0.75, 0.5) - Lighter golden
- Dry Color: RGB(0.6, 0.5, 0.3)

[Shape]
- Height Min: 0.5
- Height Max: 1.5
- Blade Width: 0.04
- Curvature: 0.4

[Wind Response]
- Wind Sensitivity: 0.8
- Sway Speed: 1.8

================================================================================
SHORT SAVANNA GRASS PRESET
================================================================================
Name: ShortSavannaGrass

[Appearance]
- Base Color: RGB(0.5, 0.45, 0.3)
- Tip Color: RGB(0.7, 0.6, 0.4)
- Dry Color: RGB(0.55, 0.45, 0.3)

[Shape]
- Height Min: 0.15
- Height Max: 0.4
- Blade Width: 0.03
- Curvature: 0.25

[Wind Response]
- Wind Sensitivity: 0.6
- Sway Speed: 2.2

*/

// Runtime helper to apply presets programmatically
namespace GlimmerDiary.Flora
{
    using UnityEngine;
    using System.Collections.Generic;
    
    public static class PlantPresets
    {
        /// <summary>
        /// Apply Baobab preset to a PlantDefinition
        /// </summary>
        public static void ApplyBaobabPreset(PlantDefinition def)
        {
            def.plantName = "Baobab";
            def.category = PlantCategory.Tree;
            
            // L-System
            def.lSystemPreset = new LSystemPreset
            {
                axiom = "X",
                rules = new List<LSystemRuleEntry>
                {
                    new() { symbol = 'X', replacement = "FFFFF[&X][/&X][//&X][///&X]" },
                    new() { symbol = 'F', replacement = "F" }
                },
                baseIterations = 3,
                minIterations = 2,
                maxIterations = 4,
                baseAngle = 35f,
                angleVariation = 10f,
                baseStepLength = 0.8f,
                lengthDecayPerIteration = 0.85f,
                use3DRotation = true,
                yawAngle = 90f,
                pitchVariation = 20f
            };
            
            // Trunk
            def.trunk = new TrunkSettings
            {
                baseRadius = 0.5f,
                radiusDecay = 0.88f,
                radialSegments = 10,
                baseBulge = 0.8f,
                twistPerSegment = 5f,
                surfaceNoise = 0.08f,
                barkTintBase = new Color(0.35f, 0.28f, 0.22f),
                barkTiling = 3f
            };
            
            // Foliage
            def.foliage = new FoliageSettings
            {
                leafShape = LeafShape.Round,
                distribution = LeafDistribution.BranchTips,
                leafSizeRange = new Vector2(0.15f, 0.35f),
                leafDensity = 0.4f,
                leavesPerCluster = 8,
                clusterSpread = 0.5f,
                leafColorBase = new Color(0.25f, 0.45f, 0.15f),
                leafColorTip = new Color(0.35f, 0.55f, 0.2f),
                useSubsurfaceScattering = true,
                translucency = 0.3f
            };
            
            // Wind
            def.windResponse = new WindResponse
            {
                trunkSwayAmount = 0.01f,
                trunkSwaySpeed = 0.3f,
                branchFlexAmount = 0.1f,
                branchFlexSpeed = 0.8f,
                leafFlutterAmount = 0.25f,
                leafFlutterSpeed = 2f
            };
            
            // Growth
            def.growth = new GrowthSettings
            {
                fullGrowthDuration = 5f,
                sequence = GrowthSequence.RootToTip,
                foliageDelay = 0.7f
            };
            
            def.valenceAffinity = 0.3f;
            def.arousalAffinity = 0.2f;
        }
        
        /// <summary>
        /// Apply Acacia preset to a PlantDefinition
        /// </summary>
        public static void ApplyAcaciaPreset(PlantDefinition def)
        {
            def.plantName = "Acacia";
            def.category = PlantCategory.Tree;
            
            def.lSystemPreset = new LSystemPreset
            {
                axiom = "X",
                rules = new List<LSystemRuleEntry>
                {
                    new() { symbol = 'X', replacement = "FFF[&FX][/&FX][//&FX][///&FX][////&FX]" },
                    new() { symbol = 'F', replacement = "F" }
                },
                baseIterations = 3,
                minIterations = 3,
                maxIterations = 5,
                baseAngle = 40f,
                angleVariation = 8f,
                baseStepLength = 0.5f,
                lengthDecayPerIteration = 0.75f,
                use3DRotation = true,
                yawAngle = 72f,
                pitchVariation = 10f
            };
            
            def.trunk = new TrunkSettings
            {
                baseRadius = 0.12f,
                radiusDecay = 0.78f,
                radialSegments = 8,
                baseBulge = 0f,
                twistPerSegment = 3f,
                surfaceNoise = 0.04f,
                barkTintBase = new Color(0.3f, 0.22f, 0.15f),
                barkTiling = 2.5f
            };
            
            def.foliage = new FoliageSettings
            {
                leafShape = LeafShape.Pointed,
                distribution = LeafDistribution.Umbrella,
                leafSizeRange = new Vector2(0.05f, 0.12f),
                leafDensity = 0.8f,
                leavesPerCluster = 15,
                clusterSpread = 0.3f,
                leafColorBase = new Color(0.3f, 0.5f, 0.2f),
                leafColorTip = new Color(0.4f, 0.6f, 0.25f),
                useSubsurfaceScattering = true,
                translucency = 0.5f
            };
            
            def.windResponse = new WindResponse
            {
                trunkSwayAmount = 0.03f,
                trunkSwaySpeed = 0.5f,
                branchFlexAmount = 0.2f,
                branchFlexSpeed = 1.2f,
                leafFlutterAmount = 0.4f,
                leafFlutterSpeed = 3f
            };
            
            def.growth = new GrowthSettings
            {
                fullGrowthDuration = 4f,
                sequence = GrowthSequence.RootToTip,
                foliageDelay = 0.5f
            };
            
            def.valenceAffinity = 0.5f;
            def.arousalAffinity = 0.4f;
        }
        
        /// <summary>
        /// Apply Shrub preset
        /// </summary>
        public static void ApplyShrubPreset(PlantDefinition def)
        {
            def.plantName = "Savanna Shrub";
            def.category = PlantCategory.Shrub;
            
            def.lSystemPreset = new LSystemPreset
            {
                axiom = "X",
                rules = new List<LSystemRuleEntry>
                {
                    new() { symbol = 'X', replacement = "F[&FX][/&FX][//&FX][///&FX]" },
                    new() { symbol = 'F', replacement = "F" }
                },
                baseIterations = 3,
                minIterations = 2,
                maxIterations = 4,
                baseAngle = 40f,
                angleVariation = 15f,
                baseStepLength = 0.3f,
                lengthDecayPerIteration = 0.8f,
                use3DRotation = true,
                yawAngle = 90f
            };
            
            def.trunk = new TrunkSettings
            {
                baseRadius = 0.03f,
                radiusDecay = 0.85f,
                radialSegments = 6,
                baseBulge = 0f,
                surfaceNoise = 0.02f,
                barkTintBase = new Color(0.4f, 0.32f, 0.2f)
            };
            
            def.foliage = new FoliageSettings
            {
                leafShape = LeafShape.Oval,
                distribution = LeafDistribution.AlongBranches,
                leafSizeRange = new Vector2(0.08f, 0.15f),
                leafDensity = 0.7f,
                leavesPerCluster = 6,
                clusterSpread = 0.15f,
                leafColorBase = new Color(0.35f, 0.5f, 0.25f),
                leafColorTip = new Color(0.5f, 0.65f, 0.3f),
                useSubsurfaceScattering = true,
                translucency = 0.4f
            };
            
            def.windResponse = new WindResponse
            {
                trunkSwayAmount = 0.08f,
                branchFlexAmount = 0.25f,
                leafFlutterAmount = 0.35f
            };
            
            def.growth = new GrowthSettings
            {
                fullGrowthDuration = 2f,
                foliageDelay = 0.3f
            };
            
            def.valenceAffinity = 0.6f;
            def.arousalAffinity = 0.5f;
        }
    }
}
