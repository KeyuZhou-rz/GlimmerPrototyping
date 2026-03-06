using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GlimmerDiary.Flora
{
    /// <summary>
    /// L-System string generator supporting:
    /// - Deterministic rules
    /// - Stochastic (weighted random) rules
    /// - Emotion-driven iteration count
    /// 
    /// This class ONLY generates the L-System string.
    /// Use TurtleInterpreter3D to convert the string into geometry.
    /// </summary>
    public static class LSystemGenerator
    {
        #region Public API
        
        /// <summary>
        /// Generate L-System string from a PlantDefinition
        /// </summary>
        /// <param name="plant">Plant definition containing L-System rules</param>
        /// <param name="overrideIterations">Optional iteration count override</param>
        /// <param name="seed">Random seed for stochastic generation</param>
        /// <returns>Generated L-System string</returns>
        public static string Generate(PlantDefinition plant, int? overrideIterations = null, int seed = 0)
        {
            if (plant == null || plant.lSystemPreset == null)
            {
                Debug.LogError("LSystemGenerator: PlantDefinition or LSystemPreset is null");
                return "F";
            }
            
            var preset = plant.lSystemPreset;
            int iterations = overrideIterations ?? preset.baseIterations;
            
            // Clamp iterations to safe range
            iterations = Mathf.Clamp(iterations, preset.minIterations, preset.maxIterations);
            
            // Build rule dictionary from preset
            var rules = new Dictionary<char, string>();
            foreach (var rule in preset.rules)
            {
                rules[rule.symbol] = rule.replacement;
            }
            
            // Choose generation method
            if (preset.useStochasticRules && preset.stochasticRules != null && preset.stochasticRules.Count > 0)
            {
                return GenerateStochastic(preset.axiom, iterations, preset.stochasticRules, seed);
            }
            
            return GenerateDeterministic(preset.axiom, iterations, rules);
        }
        
        /// <summary>
        /// Generate with emotion-driven parameters
        /// </summary>
        /// <param name="plant">Plant definition</param>
        /// <param name="valence">Emotional valence (-1 to 1)</param>
        /// <param name="arousal">Emotional arousal (0 to 1)</param>
        /// <param name="seed">Random seed</param>
        /// <returns>Generated L-System string</returns>
        public static string GenerateWithEmotion(PlantDefinition plant, float valence, float arousal, int seed = 0)
        {
            if (plant == null || plant.lSystemPreset == null)
            {
                return "F";
            }
            
            var preset = plant.lSystemPreset;
            
            // Map emotion to iteration count
            // High valence + high arousal = more growth (more iterations)
            // Low valence + low arousal = sparse growth (fewer iterations)
            float emotionFactor = (valence + 1f) * 0.5f * 0.7f + arousal * 0.3f; // 0 to 1
            
            int iterationRange = preset.maxIterations - preset.minIterations;
            int iterations = preset.minIterations + Mathf.RoundToInt(iterationRange * emotionFactor);
            iterations = Mathf.Clamp(iterations, preset.minIterations, preset.maxIterations);
            
            return Generate(plant, iterations, seed);
        }
        
        #endregion
        
        #region Generation Methods
        
        /// <summary>
        /// Standard deterministic L-System generation
        /// </summary>
        public static string GenerateDeterministic(string axiom, int iterations, Dictionary<char, string> rules)
        {
            if (string.IsNullOrEmpty(axiom))
            {
                return "F";
            }
            
            string current = axiom;
            var next = new StringBuilder(current.Length * 4);
            
            for (int i = 0; i < iterations; i++)
            {
                next.Clear();
                next.EnsureCapacity(current.Length * 2);
                
                foreach (char c in current)
                {
                    if (rules.TryGetValue(c, out string replacement))
                    {
                        next.Append(replacement);
                    }
                    else
                    {
                        next.Append(c);
                    }
                }
                
                current = next.ToString();
                
                // Safety check: prevent runaway growth
                if (current.Length > 100000)
                {
                    Debug.LogWarning($"LSystemGenerator: String exceeded 100k chars at iteration {i + 1}, stopping early");
                    break;
                }
            }
            
            return current;
        }
        
        /// <summary>
        /// Stochastic L-System generation with weighted rule selection
        /// </summary>
        public static string GenerateStochastic(
            string axiom, 
            int iterations, 
            List<StochasticRuleEntry> stochasticRules,
            int seed)
        {
            if (string.IsNullOrEmpty(axiom))
            {
                return "F";
            }
            
            var random = new System.Random(seed);
            
            // Pre-process rules into lookup with cumulative weights
            var ruleLookup = new Dictionary<char, List<(string replacement, float cumulativeWeight)>>();
            
            foreach (var entry in stochasticRules)
            {
                var options = new List<(string, float)>();
                float totalWeight = 0f;
                
                foreach (var opt in entry.options)
                {
                    totalWeight += opt.weight;
                    options.Add((opt.replacement, totalWeight));
                }
                
                // Normalize to 0-1 range
                if (totalWeight > 0f)
                {
                    for (int i = 0; i < options.Count; i++)
                    {
                        options[i] = (options[i].Item1, options[i].Item2 / totalWeight);
                    }
                }
                
                ruleLookup[entry.symbol] = options;
            }
            
            string current = axiom;
            var next = new StringBuilder();
            
            for (int i = 0; i < iterations; i++)
            {
                next.Clear();
                
                foreach (char c in current)
                {
                    if (ruleLookup.TryGetValue(c, out var options) && options.Count > 0)
                    {
                        float roll = (float)random.NextDouble();
                        string chosen = options[options.Count - 1].replacement; // Default to last
                        
                        foreach (var (replacement, cumulativeWeight) in options)
                        {
                            if (roll <= cumulativeWeight)
                            {
                                chosen = replacement;
                                break;
                            }
                        }
                        next.Append(chosen);
                    }
                    else
                    {
                        next.Append(c);
                    }
                }
                
                current = next.ToString();
                
                // Safety check
                if (current.Length > 100000)
                {
                    Debug.LogWarning($"LSystemGenerator: Stochastic string exceeded 100k chars at iteration {i + 1}");
                    break;
                }
            }
            
            return current;
        }
        
        #endregion
        
        #region Preset Rule Sets
        
        /// <summary>
        /// Pre-built rule sets for common plant types.
        /// Use these as starting points for custom PlantDefinitions.
        /// </summary>
        public static class Presets
        {
            /// <summary>
            /// Classic binary tree - symmetric branching
            /// Good for: Generic trees, teaching examples
            /// </summary>
            public static Dictionary<char, string> BinaryTree => new()
            {
                { 'X', "F[+X][-X]FX" },
                { 'F', "FF" }
            };
            
            /// <summary>
            /// Natural looking tree with asymmetric growth
            /// Good for: General purpose trees
            /// </summary>
            public static Dictionary<char, string> NaturalTree => new()
            {
                { 'X', "F+[[X]-X]-F[-FX]+X" },
                { 'F', "FF" }
            };
            
            /// <summary>
            /// Baobab - thick trunk with delayed sparse branching
            /// Good for: African savanna centerpiece
            /// Use with: High baseRadius, useThicknessCurve=true
            /// </summary>
            public static Dictionary<char, string> Baobab => new()
            {
                { 'X', "FFFF[+X][-X][^X]" },  // Long trunk, sparse branches
                { 'F', "F" }  // No trunk multiplication
            };
            
            /// <summary>
            /// Acacia - horizontal umbrella canopy
            /// Good for: Savanna trees
            /// Use with: useTropism=true, tropismDirection=up
            /// </summary>
            public static Dictionary<char, string> Acacia => new()
            {
                { 'X', "FF[++X][--X][+X][-X]" },  // Wide horizontal spread
                { 'F', "F" }
            };
            
            /// <summary>
            /// Shrub - multi-stem, bushy, low to ground
            /// Good for: Ground cover, underbrush
            /// </summary>
            public static Dictionary<char, string> Shrub => new()
            {
                { 'X', "[+FX][-FX][++FX][--FX]" },
                { 'F', "F" }
            };
            
            /// <summary>
            /// Dense bush - very compact
            /// Good for: Hedges, thick vegetation
            /// </summary>
            public static Dictionary<char, string> DenseBush => new()
            {
                { 'X', "[+FX][-FX][^FX][&FX]" },
                { 'F', "F" }
            };
            
            /// <summary>
            /// Weeping tree - drooping branches
            /// Good for: Willows, emotional sad scenes
            /// Use with: useTropism=true, tropismDirection=down
            /// </summary>
            public static Dictionary<char, string> Weeping => new()
            {
                { 'X', "F[+X][--X]F[--X][+X]" },
                { 'F', "FF" }
            };
            
            /// <summary>
            /// Conifer/Pine - layered horizontal branches
            /// Good for: Evergreen forests
            /// </summary>
            public static Dictionary<char, string> Conifer => new()
            {
                { 'X', "F[&+X][&-X][&++X][&--X]FX" },  // & = pitch down
                { 'F', "F" }
            };
            
            /// <summary>
            /// Tall grass / Reed
            /// Good for: Wetlands, meadows
            /// </summary>
            public static Dictionary<char, string> TallGrass => new()
            {
                { 'X', "F[+X]F[-X]X" },
                { 'F', "F" }
            };
            
            /// <summary>
            /// Spiral vine
            /// Good for: Climbing plants, decorative elements
            /// </summary>
            public static Dictionary<char, string> Vine => new()
            {
                { 'X', "F/[+X]F/[-X]X" },
                { 'F', "F" }
            };
        }
        
        #endregion
        
        #region Utility Methods
        
        /// <summary>
        /// Analyze an L-System string to predict complexity
        /// </summary>
        public static LSystemStats AnalyzeString(string lSystemString)
        {
            var stats = new LSystemStats();
            
            if (string.IsNullOrEmpty(lSystemString))
                return stats;
            
            stats.totalLength = lSystemString.Length;
            int currentDepth = 0;
            
            foreach (char c in lSystemString)
            {
                switch (c)
                {
                    case 'F':
                    case 'G':
                        stats.segmentCount++;
                        break;
                    case '[':
                        currentDepth++;
                        stats.maxDepth = Mathf.Max(stats.maxDepth, currentDepth);
                        stats.branchCount++;
                        break;
                    case ']':
                        currentDepth--;
                        break;
                    case '+':
                    case '-':
                        stats.rotationCount++;
                        break;
                }
            }
            
            return stats;
        }
        
        public struct LSystemStats
        {
            public int totalLength;
            public int segmentCount;
            public int branchCount;
            public int maxDepth;
            public int rotationCount;
            
            public override string ToString()
            {
                return $"Length: {totalLength}, Segments: {segmentCount}, Branches: {branchCount}, MaxDepth: {maxDepth}";
            }
        }
        
        #endregion
    }
}