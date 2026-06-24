using System.Collections.Generic;
using UnityEngine;

namespace GlimmerDiary.Flora
{
    /// <summary>
    /// Advanced 3D Turtle Interpreter for L-System strings.
    /// 
    /// Converts L-System strings into BranchSegment geometry data.
    /// Each segment knows its parent, enabling proper mesh vertex sharing.
    /// 
    /// Symbol Reference:
    /// F, G    = Move forward and draw segment
    /// f       = Move forward without drawing
    /// +       = Yaw right (rotate around local UP axis)
    /// -       = Yaw left
    /// ^       = Pitch up (nose up, rotate around local RIGHT axis)
    /// &       = Pitch down (nose down)
    /// /       = Roll right (rotate around local FORWARD axis)
    /// \       = Roll left
    /// |       = Turn around 180°
    /// [       = Push state onto stack (start branch)
    /// ]       = Pop state from stack (end branch)
    /// !       = Decrement diameter (optional explicit control)
    /// </summary>
    public class TurtleInterpreter3D
    {
        #region Data Structures
        
        /// <summary>
        /// Represents a single branch segment with all geometric data.
        /// The parentSegmentIndex is CRITICAL for mesh vertex sharing.
        /// </summary>
        public struct BranchSegment
        {
            public Vector3 start;
            public Vector3 end;
            public Quaternion orientation;
            public float startRadius;
            public float endRadius;
            public int depth;                    // Branch depth (0 = trunk)
            public int branchIndex;              // Per-generation counter (NOT stable across regen)
            public string branchPath;            // Stable path id, e.g. "0/2/1". Survives regeneration
                                                 // as long as the rule's branch structure is unchanged.
                                                 // Use this to address a specific limb for growth/break events.
            public float normalizedPosition;     // 0 = root, 1 = tip (for UV2/growth)
            public float distanceFromRoot;       // Actual distance traveled from root
            public int parentSegmentIndex;       // Index of parent segment (-1 for root)
        }
        
        /// <summary>
        /// Internal turtle state for stack operations
        /// </summary>
        private struct TurtleState
        {
            public Vector3 position;
            public Quaternion rotation;
            public float currentRadius;
            public int depth;
            public int segmentCount;
            public float distanceFromRoot;
            public int lastSegmentIndex;
            public string branchPath;    // Stable path id of the branch currently being drawn
            public int childCount;       // How many child branches this branch has already spawned

            public TurtleState Clone()
            {
                return new TurtleState
                {
                    position = position,
                    rotation = rotation,
                    currentRadius = currentRadius,
                    depth = depth,
                    segmentCount = segmentCount,
                    distanceFromRoot = distanceFromRoot,
                    lastSegmentIndex = lastSegmentIndex,
                    branchPath = branchPath,
                    childCount = childCount
                };
            }
        }
        
        /// <summary>
        /// Configuration parameters for interpretation
        /// </summary>
        public class InterpreterConfig
        {
            // Basic geometry
            public float baseAngle = 25f;
            public float stepLength = 0.5f;
            public float initialRadius = 0.15f;
            public float radiusDecay = 0.75f;
            
            // 3D rotation angles
            public float yawAngle = 25f;
            public float pitchAngle = 25f;
            public float rollAngle = 25f;
            
            // Variation for organic feel
            public float angleVariance = 0f;
            public float lengthVariance = 0f;
            public float radiusVariance = 0f;
            
            // Advanced: Width curve (optional)
            public bool useWidthCurve = false;
            public AnimationCurve widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.1f);
            
            // Advanced: Tropism (optional)
            public bool useTropism = false;
            public Vector3 tropismDirection = Vector3.up;
            public float tropismStrength = 0.1f;
            
            // Length decay per depth
            public float lengthDecayPerDepth = 0.9f;
            
            // Random seed
            public int seed = 0;
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Interpret L-System string using a PlantDefinition
        /// </summary>
        public List<BranchSegment> Interpret(string lSystemString, PlantDefinition plant, float emotionalScale = 1f, int seed = 0)
        {
            if (plant == null)
            {
                Debug.LogError("TurtleInterpreter3D: PlantDefinition is null");
                return new List<BranchSegment>();
            }
            
            var config = CreateConfigFromPlant(plant, emotionalScale, seed);
            return Interpret(lSystemString, config);
        }
        
        /// <summary>
        /// Interpret L-System string with explicit configuration
        /// </summary>
        public List<BranchSegment> Interpret(string lSystemString, InterpreterConfig config)
        {
            if (string.IsNullOrEmpty(lSystemString))
            {
                Debug.LogWarning("TurtleInterpreter3D: Empty L-System string");
                return new List<BranchSegment>();
            }
            
            if (config == null)
            {
                config = new InterpreterConfig();
            }
            
            var segments = new List<BranchSegment>();
            var stateStack = new Stack<TurtleState>();
            var random = new System.Random(config.seed);
            
            // Initialize turtle at origin, facing UP (Y+)
            var state = new TurtleState
            {
                position = Vector3.zero,
                rotation = Quaternion.identity,
                currentRadius = config.initialRadius,
                depth = 0,
                segmentCount = 0,
                distanceFromRoot = 0f,
                lastSegmentIndex = -1,
                branchPath = "0",   // trunk; children become "0/0", "0/1", grandchildren "0/0/0", ...
                childCount = 0
            };
            
            // First pass: calculate total distance for normalization
            float totalDistance = CalculateTotalDistance(lSystemString, config);
            
            int branchIndex = 0;
            
            // Process each symbol
            foreach (char symbol in lSystemString)
            {
                // Calculate variation for this step
                float angleVar = config.angleVariance > 0 
                    ? (float)(random.NextDouble() * 2 - 1) * config.angleVariance 
                    : 0f;
                float lengthVar = config.lengthVariance > 0 
                    ? (float)(random.NextDouble() * 2 - 1) * config.lengthVariance 
                    : 0f;
                float radiusVar = config.radiusVariance > 0
                    ? (float)(random.NextDouble() * 2 - 1) * config.radiusVariance
                    : 0f;
                
                switch (symbol)
                {
                    // === MOVEMENT COMMANDS ===
                    case 'F':
                    case 'G':
                        ProcessForwardDraw(
                            ref state, 
                            segments, 
                            config, 
                            totalDistance,
                            lengthVar, 
                            radiusVar,
                            branchIndex
                        );
                        break;
                        
                    case 'f':
                        ProcessForwardNoDraw(ref state, config, lengthVar);
                        break;
                    
                    // === YAW (Rotation around local UP axis) ===
                    case '+':
                        ApplyYaw(ref state, config.yawAngle + angleVar);
                        break;

                    case '-':
                        ApplyYaw(ref state, -(config.yawAngle + angleVar));
                        break;

                    // === PITCH (Rotation around local RIGHT axis) ===
                    case '^':
                        ApplyPitch(ref state, config.pitchAngle + angleVar);
                        break;

                    case '&':
                        ApplyPitch(ref state, -(config.pitchAngle + angleVar));
                        break;
                    
                    // === ROLL (Rotation around local FORWARD axis) ===
                    case '/':
                        ApplyRoll(ref state, config.rollAngle + angleVar);
                        break;
                        
                    case '\\':
                        ApplyRoll(ref state, -(config.rollAngle + angleVar));
                        break;
                    
                    // === TURN AROUND ===
                    case '|':
                        ApplyYaw(ref state, 180f);
                        break;
                    
                    // === BRANCH STACK ===
                    case '[':
                        // This new branch is the Nth child of the current branch.
                        int childSlot = state.childCount;
                        state.childCount++;                 // count it so the next sibling gets the next slot
                        stateStack.Push(state.Clone());     // push parent (with updated childCount) for ']'
                        state.depth++;
                        state.branchPath = state.branchPath + "/" + childSlot;
                        state.childCount = 0;               // the child starts with no children of its own
                        branchIndex++;
                        break;

                    case ']':
                        if (stateStack.Count > 0)
                        {
                            state = stateStack.Pop();
                        }
                        else
                        {
                            Debug.LogWarning("TurtleInterpreter3D: Stack underflow at ']'");
                        }
                        break;
                    
                    // === DIAMETER CONTROL ===
                    case '!':
                        state.currentRadius *= config.radiusDecay;
                        break;
                        
                    // === IGNORED SYMBOLS (variables/placeholders) ===
                    case 'X':
                    case 'Y':
                    case 'Z':
                    case 'A':
                    case 'B':
                    case 'C':
                        break;
                        
                    default:
                        break;
                }
            }
            
            // Post-process: normalize positions
            NormalizeSegmentData(segments, totalDistance);
            
            return segments;
        }
        
        /// <summary>
        /// Create configuration from PlantDefinition.
        /// Only uses fields that exist in your TrunkSettings class.
        /// </summary>
        public InterpreterConfig CreateConfigFromPlant(PlantDefinition plant, float emotionalScale = 1f, int seed = 0)
        {
            var preset = plant.lSystemPreset;
            var trunk = plant.trunk;
            
            var config = new InterpreterConfig
            {
                // Basic geometry - scale by emotion
                baseAngle = preset.baseAngle,
                stepLength = preset.baseStepLength * emotionalScale,
                initialRadius = trunk.baseRadius * emotionalScale,
                radiusDecay = trunk.radiusDecay,
                
                // 3D angles
                yawAngle = preset.baseAngle,
                pitchAngle = preset.baseAngle,
                rollAngle = preset.yawAngle,
                
                // Variation
                angleVariance = preset.angleVariation,
                lengthVariance = 0f,
                radiusVariance = 0f,
                
                // Length decay
                lengthDecayPerDepth = preset.lengthDecayPerIteration,
                
                // Seed
                seed = seed,

                // Width curve (baobab trunk support) and tropism are wired from TrunkSettings.
                // Both default to off on existing assets, so plants that don't opt in are unaffected.
                useWidthCurve = trunk.useThicknessCurve,
                widthCurve = trunk.thicknessCurve,
                useTropism = trunk.useTropism,
                tropismDirection = trunk.tropismDirection,
                tropismStrength = trunk.tropismStrength
            };
            
            return config;
        }
        
        #endregion
        
        #region Movement Processing
        
        private void ProcessForwardDraw(
            ref TurtleState state,
            List<BranchSegment> segments,
            InterpreterConfig config,
            float totalDistance,
            float lengthVariance,
            float radiusVariance,
            int branchIndex)
        {
            // Calculate step length with depth decay and variance
            float depthFactor = Mathf.Pow(config.lengthDecayPerDepth, state.depth);
            float currentStepLength = config.stepLength * depthFactor * (1f + lengthVariance);
            
            // Get turtle's forward direction (trees grow UP in local space)
            Vector3 forward = state.rotation * Vector3.up;
            
            // Calculate positions
            Vector3 startPos = state.position;
            Vector3 endPos = startPos + forward * currentStepLength;
            
            // Calculate radius at start and end
            float startRadius = state.currentRadius * (1f + radiusVariance);
            float endRadius;
            
            if (config.useWidthCurve)
            {
                // Sample width from curve based on normalized distance
                float normalizedStart = state.distanceFromRoot / Mathf.Max(0.001f, totalDistance);
                float normalizedEnd = (state.distanceFromRoot + currentStepLength) / Mathf.Max(0.001f, totalDistance);
                
                startRadius = config.initialRadius * config.widthCurve.Evaluate(normalizedStart);
                endRadius = config.initialRadius * config.widthCurve.Evaluate(normalizedEnd);
            }
            else
            {
                // Linear decay
                endRadius = startRadius * config.radiusDecay;
            }
            
            // Ensure minimum radius
            startRadius = Mathf.Max(startRadius, 0.001f);
            endRadius = Mathf.Max(endRadius, 0.001f);
            
            // Create segment with parent reference
            var segment = new BranchSegment
            {
                start = startPos,
                end = endPos,
                orientation = state.rotation,
                startRadius = startRadius,
                endRadius = endRadius,
                depth = state.depth,
                branchIndex = branchIndex,
                branchPath = state.branchPath,
                normalizedPosition = state.distanceFromRoot / Mathf.Max(0.001f, totalDistance),
                distanceFromRoot = state.distanceFromRoot,
                parentSegmentIndex = state.lastSegmentIndex
            };
            
            segments.Add(segment);

            // Update state for next segment
            state.position = endPos;
            state.currentRadius = endRadius;
            state.segmentCount++;
            state.distanceFromRoot += currentStepLength;
            state.lastSegmentIndex = segments.Count - 1;

            // Tropism bends the branch during growth, not during rotation
            ApplyTropism(ref state, config);
        }
        
        private void ProcessForwardNoDraw(ref TurtleState state, InterpreterConfig config, float lengthVariance)
        {
            float depthFactor = Mathf.Pow(config.lengthDecayPerDepth, state.depth);
            float currentStepLength = config.stepLength * depthFactor * (1f + lengthVariance);
            
            Vector3 forward = state.rotation * Vector3.up;
            state.position += forward * currentStepLength;
            state.distanceFromRoot += currentStepLength;
        }
        
        #endregion
        
        #region Rotation Operations
        
        private void ApplyYaw(ref TurtleState state, float angleDegrees)
        {
            Quaternion yawRotation = Quaternion.AngleAxis(angleDegrees, state.rotation * Vector3.forward);
            state.rotation = yawRotation * state.rotation;
        }
        
        private void ApplyPitch(ref TurtleState state, float angleDegrees)
        {
            Quaternion pitchRotation = Quaternion.AngleAxis(angleDegrees, state.rotation * Vector3.right);
            state.rotation = pitchRotation * state.rotation;
        }
        
        private void ApplyRoll(ref TurtleState state, float angleDegrees)
        {
            Quaternion rollRotation = Quaternion.AngleAxis(angleDegrees, state.rotation * Vector3.up);
            state.rotation = rollRotation * state.rotation;
        }
        
        private void ApplyTropism(ref TurtleState state, InterpreterConfig config)
        {
            if (!config.useTropism || config.tropismStrength <= 0f)
                return;
            
            Vector3 currentUp = state.rotation * Vector3.up;
            Vector3 tropismDir = config.tropismDirection.normalized;
            
            Vector3 rotationAxis = Vector3.Cross(currentUp, tropismDir);
            
            if (rotationAxis.sqrMagnitude < 0.0001f)
                return;
            
            rotationAxis.Normalize();
            
            float angle = Vector3.Angle(currentUp, tropismDir);
            float bendAmount = Mathf.Min(angle * config.tropismStrength, 45f);
            
            Quaternion tropismRotation = Quaternion.AngleAxis(bendAmount, rotationAxis);
            state.rotation = tropismRotation * state.rotation;
        }
        
        #endregion
        
        #region Utility Methods
        
        private float CalculateTotalDistance(string lSystemString, InterpreterConfig config)
        {
            int depth = 0;
            var depthStack = new Stack<float>();
            float currentDistance = 0f;
            float maxDistance = 0f;
            
            foreach (char symbol in lSystemString)
            {
                switch (symbol)
                {
                    case 'F':
                    case 'G':
                        float depthFactor = Mathf.Pow(config.lengthDecayPerDepth, depth);
                        currentDistance += config.stepLength * depthFactor;
                        maxDistance = Mathf.Max(maxDistance, currentDistance);
                        break;
                        
                    case 'f':
                        float depthFactor2 = Mathf.Pow(config.lengthDecayPerDepth, depth);
                        currentDistance += config.stepLength * depthFactor2;
                        break;
                        
                    case '[':
                        depthStack.Push(currentDistance);
                        depth++;
                        break;
                        
                    case ']':
                        if (depthStack.Count > 0)
                        {
                            currentDistance = depthStack.Pop();
                            depth--;
                        }
                        break;
                }
            }
            
            return maxDistance > 0f ? maxDistance : 1f;
        }
        
        private void NormalizeSegmentData(List<BranchSegment> segments, float totalDistance)
        {
            if (segments.Count == 0 || totalDistance <= 0f)
                return;
            
            float maxDist = 0f;
            foreach (var seg in segments)
            {
                float segEnd = seg.distanceFromRoot + Vector3.Distance(seg.start, seg.end);
                maxDist = Mathf.Max(maxDist, segEnd);
            }
            
            if (maxDist <= 0f)
                return;
            
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                seg.normalizedPosition = seg.distanceFromRoot / maxDist;
                segments[i] = seg;
            }
        }
        
        #endregion
        
        #region Debug Helpers
        
        public static void DrawGizmos(List<BranchSegment> segments, Transform transform, bool showRadius = true, bool showParentLinks = false)
        {
            if (segments == null) return;
            
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                Vector3 worldStart = transform.TransformPoint(seg.start);
                Vector3 worldEnd = transform.TransformPoint(seg.end);
                
                float hue = (seg.depth * 0.15f) % 1f;
                Gizmos.color = Color.HSVToRGB(hue, 0.8f, 0.9f);
                
                Gizmos.DrawLine(worldStart, worldEnd);
                
                if (showRadius)
                {
                    Gizmos.DrawWireSphere(worldStart, seg.startRadius * 0.5f);
                    Gizmos.DrawWireSphere(worldEnd, seg.endRadius * 0.5f);
                }
                
                if (showParentLinks && seg.parentSegmentIndex >= 0 && seg.parentSegmentIndex < segments.Count)
                {
                    var parent = segments[seg.parentSegmentIndex];
                    Vector3 parentEnd = transform.TransformPoint(parent.end);
                    
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawLine(worldStart, parentEnd);
                }
            }
        }
        
        public static string GetStatistics(List<BranchSegment> segments)
        {
            if (segments == null || segments.Count == 0)
                return "No segments";
            
            int maxDepth = 0;
            float totalLength = 0f;
            float minRadius = float.MaxValue;
            float maxRadius = 0f;
            int orphanCount = 0;
            
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                maxDepth = Mathf.Max(maxDepth, seg.depth);
                totalLength += Vector3.Distance(seg.start, seg.end);
                minRadius = Mathf.Min(minRadius, Mathf.Min(seg.startRadius, seg.endRadius));
                maxRadius = Mathf.Max(maxRadius, Mathf.Max(seg.startRadius, seg.endRadius));
                
                if (i > 0 && seg.parentSegmentIndex < 0)
                {
                    orphanCount++;
                }
            }
            
            return $"Segments: {segments.Count}, MaxDepth: {maxDepth}, " +
                   $"TotalLength: {totalLength:F2}, Radius: [{minRadius:F3} - {maxRadius:F3}], " +
                   $"Orphans: {orphanCount}";
        }
        
        #endregion
    }
}