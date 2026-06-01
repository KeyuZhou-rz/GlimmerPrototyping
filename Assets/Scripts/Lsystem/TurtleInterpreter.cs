using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class TurtleInterpreter
{
    public struct BranchSegment
    {
        public Vector3 start;
        public Vector3 end;
        public float startWidth;
        public float endWidth;
        public int depth;  // For UV encoding (growth animation)
        public bool leafJudge; // to judge whether the leaves should be attached or not
    }

    private struct TurtleState
    {
        public Vector3 position;
        public Quaternion rotation;
        public float width;
        public int depth;
    }

    public List<BranchSegment> Interpret(
        string lSystemString,
        float angle,
        float stepLength,
        float initialWidth,
        float widthDecay,
        float angleVariance = 0f,
        float lengthVariance = 0f,
        int seed = 0)
    {
        var segments = new List<BranchSegment>();
        var stateStack = new Stack<TurtleState>();
        var random = new System.Random(seed);

        var state = new TurtleState
        {
            position = Vector3.zero,
            rotation = Quaternion.identity,  // Facing up (Y+)
            width = initialWidth,
            depth = 0
        };

        int maxDepth = 0;

        foreach (char c in lSystemString)
        {
            switch (c)
            {
                case 'F':  // Move forward and draw
                    float currentLength = stepLength * (1f + RandomRange(random, lengthVariance));
                    float endWidth = state.width * widthDecay;

                    Vector3 direction = state.rotation * Vector3.up;
                    Vector3 endPos = state.position + direction * currentLength;
                    bool DepthJudge = state.depth >= 5;

                    segments.Add(new BranchSegment
                    {
                        start = state.position,
                        end = endPos,
                        startWidth = state.width,
                        endWidth = endWidth,
                        depth = state.depth,
                        leafJudge = DepthJudge
                    });

                    state.position = endPos;
                    state.width = endWidth;
                    state.depth++;
                    maxDepth = Mathf.Max(maxDepth, state.depth);
                    
                    break;

                case '+':  // Rotate right (around Z axis for 2D tree)
                    float rightAngle = angle * (1f + RandomRange(random, angleVariance));
                    state.rotation *= Quaternion.Euler(0, 0, -rightAngle);
                    break;

                case '-':  // Rotate left
                    float leftAngle = angle * (1f + RandomRange(random, angleVariance));
                    state.rotation *= Quaternion.Euler(0, 0, leftAngle);
                    break;

                case '[':  // Push state
                    stateStack.Push(state);
                    float randomRoll = RandomRange(random, 180f);
                    state.rotation *= Quaternion.Euler(0,randomRoll,0);
                    break;

                case ']':  // Pop state
                    if (stateStack.Count > 0)
                        state = stateStack.Pop();
                    break;

                // X and other symbols are ignored (structural only)
            }
        }

        // Normalize depth values for UV encoding
        if (maxDepth > 0)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                seg.depth = Mathf.RoundToInt((float)seg.depth / maxDepth * 100);
                segments[i] = seg;
            }
        }

        return segments;
    }

    private float RandomRange(System.Random random, float variance)
    {
        if (variance <= 0) return 0;
        return (float)(random.NextDouble() * 2 - 1) * variance;
    }
}