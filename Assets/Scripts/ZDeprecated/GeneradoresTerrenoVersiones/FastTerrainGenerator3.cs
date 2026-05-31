using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Terrain))]
public class FastTerrainGenerator3 : MonoBehaviour
{
    [Header("Configuración TFG (OE2)")]
    public int seed = 42;
    public float noiseScale = 0.8f;     // Escala alta para mapa de 20m
    public float noiseAmplitude = 5f;   // Altura real en metros
    public float maxSlopeDegrees = 25f;

    [Header("Referencias")]
    public GameObject robot;
    public Transform spawnPoint;
    public Transform goalPoint;
    public float safeRadius = 1.5f;

    private Terrain terrain;
    private TerrainData terrainData;

    void Awake()
    {
        terrain = GetComponent<Terrain>();

        // Creamos la instancia y forzamos el tamaño (OE1)
        terrainData = Instantiate(terrain.terrainData);
        terrainData.size = new Vector3(20, 10, 20); // 20x20m con 10m de altura máx [cite: 6]

        terrain.terrainData = terrainData;
        if (TryGetComponent<TerrainCollider>(out var col)) col.terrainData = terrainData;
    }

    void Start() => GenerateTerrain(seed);

    public void GenerateTerrain(int newSeed)
    {
        seed = newSeed;
        Random.InitState(seed);

        // 1. Generar Alturas
        int res = terrainData.heightmapResolution;
        float[,] heights = new float[res, res];

        for (int i = 0; i < res; i++)
        {
            for (int j = 0; j < res; j++)
            {
                float x = (float)i / res * (noiseScale * 5f) + seed;
                float y = (float)j / res * (noiseScale * 5f) + seed;

                // Normalización: (valor en metros / altura máxima del terreno)
                float rawHeight = Mathf.PerlinNoise(x, y) * noiseAmplitude;
                heights[i, j] = Mathf.Clamp01(rawHeight / terrainData.size.y);
            }
        }

        // 2. Aplicar y forzar actualización visual
        terrainData.SetHeights(0, 0, heights);

        // 3. Zonas Seguras (OE4)
        ApplyFlattening();

        // 4. Forzar que Unity redibuje el terreno 
        terrainData.SyncHeightmap();
        terrain.Flush();

        TeleportRobot();
        Debug.Log($"Terreno generado. Altura máx configurada: {terrainData.size.y}m");
    }

    private void ApplyFlattening()
    {
        FlattenArea(spawnPoint.position, safeRadius);
        FlattenArea(goalPoint.position, safeRadius);
    }

    private void FlattenArea(Vector3 worldPos, float radius)
    {
        int res = terrainData.heightmapResolution;
        Vector3 localPos = worldPos - transform.position;
        int cX = Mathf.RoundToInt((localPos.x / terrainData.size.x) * res);
        int cZ = Mathf.RoundToInt((localPos.z / terrainData.size.z) * res);
        int rNodes = Mathf.RoundToInt((radius / terrainData.size.x) * res);

        for (int i = cX - rNodes; i <= cX + rNodes; i++)
        {
            for (int j = cZ - rNodes; j <= cZ + rNodes; j++)
            {
                if (i >= 0 && i < res && j >= 0 && j < res)
                {
                    float dist = Vector2.Distance(new Vector2(i, j), new Vector2(cX, cZ));
                    if (dist < rNodes)
                    {
                        float t = Mathf.SmoothStep(0, 1, dist / rNodes);
                        float currentH = terrainData.GetHeight(i, j) / terrainData.size.y;
                        terrainData.SetHeights(i, j, new float[,] { { Mathf.Lerp(0, currentH, t) } });
                    }
                }
            }
        }
    }

    public void TeleportRobot()
    {
        float h = terrain.SampleHeight(spawnPoint.position) + transform.position.y;
        robot.transform.position = new Vector3(spawnPoint.position.x, h + 0.5f, spawnPoint.position.z);

        var ab = robot.GetComponentInChildren<ArticulationBody>();
        if (ab != null) ab.TeleportRoot(robot.transform.position, Quaternion.identity); // [cite: 6, 21]
    }
}