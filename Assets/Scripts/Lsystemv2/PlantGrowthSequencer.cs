using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GlimmerDiary.Flora
{
    /// <summary>
    /// Orchestrates sequential or simultaneous growth animation for multiple PlantControllers.
    /// Drop this onto an empty GameObject, then assign PlantControllers in the inspector.
    /// </summary>
    public class PlantGrowthSequencer : MonoBehaviour
    {
        [System.Serializable]
        public class PlantSlot
        {
            public PlantController controller;
            [Tooltip("Delay in seconds before this plant starts growing")]
            [Range(0f, 10f)] public float startDelay = 0f;
        }

        [Header("Plants")]
        public List<PlantSlot> plants = new();

        [Header("Sequence Settings")]
        [Tooltip("Automatically play growth sequence on Start")]
        public bool autoPlayOnStart = true;
        [Tooltip("Loop the sequence indefinitely")]
        public bool loop = false;
        [Tooltip("Pause between loop cycles (seconds)")]
        [Range(0f, 10f)] public float loopPause = 2f;

        private Coroutine _sequenceCoroutine;

private void Start()
        {
            // Auto-discover PlantControllers in children if list is empty
            if (plants.Count == 0)
            {
                var controllers = GetComponentsInChildren<PlantController>();
                float delay = 0f;
                foreach (var ctrl in controllers)
                {
                    plants.Add(new PlantSlot { controller = ctrl, startDelay = delay });
                    delay += 1.5f; // stagger each plant by 1.5 seconds
                }
                Debug.Log($"[PlantGrowthSequencer] Auto-discovered {controllers.Length} PlantController(s).");
            }

            if (autoPlayOnStart)
                PlaySequence();
        }

        [ContextMenu("Play Growth Sequence")]
        public void PlaySequence()
        {
            if (_sequenceCoroutine != null)
                StopCoroutine(_sequenceCoroutine);
            _sequenceCoroutine = StartCoroutine(RunSequence());
        }

        [ContextMenu("Reset All Plants")]
        public void ResetAll()
        {
            if (_sequenceCoroutine != null)
            {
                StopCoroutine(_sequenceCoroutine);
                _sequenceCoroutine = null;
            }

            foreach (var slot in plants)
            {
                if (slot.controller != null)
                    slot.controller.growthProgress = 0f;
            }
        }

        [ContextMenu("Complete All Plants")]
        public void CompleteAll()
        {
            if (_sequenceCoroutine != null)
            {
                StopCoroutine(_sequenceCoroutine);
                _sequenceCoroutine = null;
            }

            foreach (var slot in plants)
            {
                if (slot.controller != null)
                    slot.controller.CompleteGrowth();
            }
        }

        private IEnumerator RunSequence()
        {
            do
            {
                // Reset all plants to zero growth first
                foreach (var slot in plants)
                {
                    if (slot.controller != null)
                        slot.controller.growthProgress = 0f;
                }

                // Launch each plant with its delay (all in parallel using their own Update loop)
                foreach (var slot in plants)
                {
                    if (slot.controller == null) continue;
                    StartCoroutine(DelayedGrow(slot.controller, slot.startDelay));
                }

                // Wait until all plants have finished growing
                float maxDuration = 0f;
                foreach (var slot in plants)
                {
                    if (slot.controller?.plantDefinition == null) continue;
                    float duration = slot.startDelay + slot.controller.plantDefinition.growth.fullGrowthDuration;
                    if (duration > maxDuration) maxDuration = duration;
                }
                yield return new WaitForSeconds(maxDuration + 0.5f);

                if (loop)
                    yield return new WaitForSeconds(loopPause);

            } while (loop);
        }

        private IEnumerator DelayedGrow(PlantController plant, float delay)
        {
            if (delay > 0f)
                yield return new WaitForSeconds(delay);

            plant.growthProgress = 0f;
            // PlantController.Update() takes care of incrementing growthProgress each frame
        }
    }
}
