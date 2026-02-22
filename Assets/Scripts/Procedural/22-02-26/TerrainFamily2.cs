using UnityEngine;

[CreateAssetMenu(fileName = "TerrainFamily2", menuName = "Scriptable Objects/TerrainFamily2")]
public class TerrainFamily2 : ScriptableObject
{
    [Header("Configuración de Elevación")]
    public float maxSlope = 5f;        // Inclinación general del terreno
    public float roughnessAmplitude = 2f; // Altura de los baches
    public float roughnessFrequency = 0.1f; // Frecuencia del ruido

    [Header("Obstáculos")]
    public float obstacleDensity = 0.1f; // 0 a 1 (probabilidad)
    public GameObject[] obstaclePrefabs;

    [Header("Garantía de Éxito")]
    public float safeRadius = 5f; // Radio plano en Inicio y Fin
}