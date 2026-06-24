using UnityEngine;
using System.Collections.Generic;

namespace GlimmerDiary.Data
{
    public class TreeData
    {
        public Vector3 position;
        public int speciesIndex;
        public float vitality = 1f;   // 0..1, drives appearance / withering (decoupled, MaterialPropertyBlock path)

        // Fixed birth-time threshold for reversible emotion/season gating.
        // Populate once at max foliage; later: go.SetActive(showAt < emotionDensity).
        // Trees appear/hide in a stable order and are never re-sampled.
        public float showAt = 1f;
    }

}