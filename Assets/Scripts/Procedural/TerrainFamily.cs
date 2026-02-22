using UnityEngine;

[CreateAssetMenu(fileName = "NuevaFamilia", menuName = "TerraRover/Familia de Terreno")]
public class TerrainFamily : ScriptableObject
{
    [Header("Configuración de Rugosidad")]
    [Range(0.01f, 1f)] public float noiseScale = 0.1f; // Frecuencia de los baches
    public float noiseAmplitude = 2.0f;               // Altura de los baches

    [Header("Configuración de Pendiente")]
    [Range(0, 30)] public float maxSlopeDegrees = 10f; // Inclinación máxima

    [Header("Obstáculos")]
    public float obstacleDensity = 0.05f; // 0.05 = 5% de probabilidad
    public GameObject[] obstaclePrefabs;  // Arrastra aquí tus prefabs de rocas
}