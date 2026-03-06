using UnityEngine;

namespace GlimmerDiary.Flora
{
    /// <summary>
    /// Global wind controller that provides consistent wind data to all vegetation.
    /// Supports gusts, directional changes, and emotion-driven intensity.
    /// </summary>
    public class WindSystem : MonoBehaviour
    {
        public static WindSystem Instance { get; private set; }
        
        [Header("Base Wind")]
        public Vector3 baseWindDirection = new Vector3(1, 0, 0.3f);
        [Range(0f, 5f)] public float baseStrength = 1f;
        [Range(0.1f, 5f)] public float baseFrequency = 1f;
        
        [Header("Gusts")]
        public bool enableGusts = true;
        [Range(0f, 3f)] public float gustStrength = 1.5f;
        [Range(0.5f, 10f)] public float gustDuration = 2f;
        [Range(5f, 30f)] public float gustInterval = 10f;
        
        [Header("Turbulence")]
        [Range(0f, 1f)] public float turbulenceAmount = 0.3f;
        [Range(0.1f, 2f)] public float turbulenceScale = 0.5f;
        
        [Header("Emotion Influence")]
        [Range(0f, 2f)] public float arousalMultiplier = 1.5f;
        [Range(-1f, 1f)] public float currentValence = 0f;
        [Range(0f, 1f)] public float currentArousal = 0.3f;
        
        // Current computed wind state
        public Vector4 CurrentWind { get; private set; }
        public float CurrentStrength { get; private set; }
        public float CurrentGustFactor { get; private set; }
        
        // Shader property IDs
        private static readonly int GlobalWindID = Shader.PropertyToID("_GlobalWind");
        private static readonly int GlobalWindStrengthID = Shader.PropertyToID("_GlobalWindStrength");
        private static readonly int GlobalWindFrequencyID = Shader.PropertyToID("_GlobalWindFrequency");
        private static readonly int GlobalWindGustID = Shader.PropertyToID("_GlobalWindGust");
        private static readonly int GlobalTimeID = Shader.PropertyToID("_GlobalTime");
        
        // Internal state
        private float _gustTimer;
        private float _currentGustProgress;
        private bool _isGusting;
        private Vector3 _gustDirection;
        
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }
        
        private void Update()
        {
            UpdateWind();
            UpdateShaderGlobals();
        }
        
        private void UpdateWind()
        {
            float time = Time.time;
            
            // Base wind with perlin noise variation
            Vector3 windDir = baseWindDirection.normalized;
            float noiseX = Mathf.PerlinNoise(time * turbulenceScale, 0) * 2 - 1;
            float noiseZ = Mathf.PerlinNoise(0, time * turbulenceScale) * 2 - 1;
            
            windDir += new Vector3(noiseX, 0, noiseZ) * turbulenceAmount;
            windDir.Normalize();
            
            // Calculate base strength with emotion influence
            float emotionalStrength = baseStrength * (1f + currentArousal * arousalMultiplier);
            
            // Low valence = more chaotic wind
            if (currentValence < 0)
            {
                float chaosAmount = Mathf.Abs(currentValence) * 0.5f;
                windDir += new Vector3(
                    Mathf.Sin(time * 3f) * chaosAmount,
                    0,
                    Mathf.Cos(time * 2.5f) * chaosAmount
                );
                windDir.Normalize();
            }
            
            // Gust handling
            CurrentGustFactor = 0f;
            if (enableGusts)
            {
                UpdateGusts(ref emotionalStrength);
            }
            
            CurrentStrength = emotionalStrength;
            CurrentWind = new Vector4(windDir.x, windDir.y, windDir.z, CurrentStrength);
        }
        
        private void UpdateGusts(ref float strength)
        {
            _gustTimer += Time.deltaTime;
            
            if (!_isGusting && _gustTimer >= gustInterval)
            {
                // Start a gust
                _isGusting = true;
                _currentGustProgress = 0f;
                _gustTimer = 0f;
                
                // Slightly randomize gust direction
                _gustDirection = baseWindDirection.normalized;
                _gustDirection = Quaternion.Euler(0, Random.Range(-30f, 30f), 0) * _gustDirection;
            }
            
            if (_isGusting)
            {
                _currentGustProgress += Time.deltaTime / gustDuration;
                
                // Gust envelope: quick rise, slow fall
                float gustEnvelope = Mathf.Sin(_currentGustProgress * Mathf.PI);
                gustEnvelope = Mathf.Pow(gustEnvelope, 0.7f);  // Sharper attack
                
                CurrentGustFactor = gustEnvelope;
                strength += gustStrength * gustEnvelope;
                
                if (_currentGustProgress >= 1f)
                {
                    _isGusting = false;
                }
            }
        }
        
        private void UpdateShaderGlobals()
        {
            // Set global shader properties so all vegetation shaders can access wind data
            Shader.SetGlobalVector(GlobalWindID, CurrentWind);
            Shader.SetGlobalFloat(GlobalWindStrengthID, CurrentStrength);
            Shader.SetGlobalFloat(GlobalWindFrequencyID, baseFrequency);
            Shader.SetGlobalFloat(GlobalWindGustID, CurrentGustFactor);
            Shader.SetGlobalFloat(GlobalTimeID, Time.time);
        }
        
        /// <summary>
        /// Set wind based on emotion state
        /// </summary>
        public void SetEmotionState(float valence, float arousal)
        {
            currentValence = valence;
            currentArousal = arousal;
        }
        
        /// <summary>
        /// Trigger an immediate strong gust (for dramatic moments)
        /// </summary>
        public void TriggerDramaticGust(float strengthMultiplier = 2f)
        {
            _isGusting = true;
            _currentGustProgress = 0f;
            gustStrength *= strengthMultiplier;
            
            // Reset after gust
            Invoke(nameof(ResetGustStrength), gustDuration + 0.1f);
        }
        
        private void ResetGustStrength()
        {
            gustStrength = 1.5f;  // Reset to default
        }
        
        /// <summary>
        /// Get wind influence at a specific world position (for localized effects)
        /// </summary>
        public Vector3 GetWindAtPosition(Vector3 worldPosition)
        {
            // Add spatial variation using noise
            float spatialNoise = Mathf.PerlinNoise(
                worldPosition.x * 0.1f + Time.time * 0.5f,
                worldPosition.z * 0.1f
            );
            
            Vector3 wind = CurrentWind;
            wind *= spatialNoise * 0.5f + 0.5f;  // 50-100% of base wind
            
            return wind;
        }
        
        private void OnDrawGizmosSelected()
        {
            // Visualize wind direction
            Gizmos.color = Color.cyan;
            Vector3 start = transform.position;
            Vector3 end = start + baseWindDirection.normalized * 5f * baseStrength;
            Gizmos.DrawLine(start, end);
            
            // Arrow head
            Vector3 right = Vector3.Cross(baseWindDirection, Vector3.up).normalized;
            Gizmos.DrawLine(end, end - baseWindDirection.normalized * 0.5f + right * 0.3f);
            Gizmos.DrawLine(end, end - baseWindDirection.normalized * 0.5f - right * 0.3f);
        }
    }
}
