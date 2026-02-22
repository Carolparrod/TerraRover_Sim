using UnityEngine;

[CreateAssetMenu(fileName = "TerrainFam4", menuName = "Scriptable Objects/TerrainFam4")]
public class TerrainFam4 : ScriptableObject
{
    [Header("Configuración de Rugosidad")]
    [Range(0.01f, 1f)] public float noiseScale = 0.1f; // Frecuencia de los baches
    public float noiseAmplitude = 2.0f;               // Altura de los baches

    [Header("Configuración de Pendiente")]
    [Range(0, 30)] public float maxSlopeDegrees = 10f; // Inclinación máxima

    [Header("Obstáculos")]
    public float obstacleDensity = 0.05f; // 0.05 = 5% de probabilidad
    public GameObject[] obstaclePrefabs;  // Arrastra aquí tus prefabs de rocas
                                          //////////////////////////////////
    /// <summary>
    /// /
    /// </summary>
    [Header("Macro-Estructura (Colinas y Valles)")]
    // Subimos el rango. Un valor de 1 a 3 creará colinas gigantes en tu mapa de 20x20
    [Range(0.1f, 5f)] public float hillScale = 1.5f;
    public float hillAmplitude = 4.0f;

    [Header("Hoyos y Cráteres")]
    // Subimos el rango. Un valor de 3 a 5 pondrá varios hoyos repartidos por el mapa
    [Range(1f, 15f)] public float holeScale = 4.0f;
    public float holeDepth = 6.0f;
    [Range(1f, 10f)] public float holeSharpness = 4.0f;
}