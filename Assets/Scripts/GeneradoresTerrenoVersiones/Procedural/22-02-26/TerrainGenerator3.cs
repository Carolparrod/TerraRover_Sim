using UnityEngine;
using System.Collections.Generic;

public class TerrainGenerator3 : MonoBehaviour
{
    public TerrainFamily terrainFamily;
    private Terrain terrain;
    private TerrainData terrainData;
    private List<GameObject> spawnedObstacles = new List<GameObject>();

    [Header("Puntos de Navegación")]
    public Transform startPoint;
    public Transform goalPoint;

    [Header("Zonas de Exclusión (Radios)")]
    public float startClearRadius = 3.0f;
    public float goalClearRadius = 3.0f;

    void Awake()
    {
        terrain = GetComponent<Terrain>();
        terrainData = Instantiate(terrain.terrainData);
        terrain.terrainData = terrainData;
        GetComponent<TerrainCollider>().terrainData = terrainData;
    }

    public void GenerateTerrain(int seed)
    {
        Random.InitState(seed);
        ClearObstacles();

        // CORRECCIÓN PERLIN NOISE: Generamos offsets pequeños para evitar la pérdida de precisión float
        float offsetX = Random.Range(0f, 9999f);
        float offsetY = Random.Range(0f, 9999f);

        int res = terrainData.heightmapResolution;
        float[,] heights = new float[res, res];

        for (int i = 0; i < res; i++)
        {
            for (int j = 0; j < res; j++)
            {
                // Usamos el offset en lugar del seed gigante
                float xCoord = (float)i / res * terrainFamily.noiseScale + offsetX;
                float yCoord = (float)j / res * terrainFamily.noiseScale + offsetY;

                float noise = Mathf.PerlinNoise(xCoord, yCoord) * terrainFamily.noiseAmplitude;
                float detailNoise = Mathf.PerlinNoise(xCoord * 10, yCoord * 10) * (terrainFamily.noiseAmplitude * 0.2f);
                float slope = (i / (float)res) * terrainData.size.x * Mathf.Tan(terrainFamily.maxSlopeDegrees * Mathf.Deg2Rad);

                heights[j, i] = Mathf.Clamp01((noise + detailNoise + slope) / terrainData.size.y);
            }
        }
        terrainData.SetHeights(0, 0, heights);

        SpawnObstacles(seed);
    }

    void SpawnObstacles(int seed)
    {
        Vector3 terrainSize = terrainData.size;
        Vector2 startPos2D = startPoint != null ? new Vector2(startPoint.localPosition.x, startPoint.localPosition.z) : Vector2.zero;
        Vector2 goalPos2D = goalPoint != null ? new Vector2(goalPoint.localPosition.x, goalPoint.localPosition.z) : Vector2.zero;

        for (float x = 2; x < terrainSize.x - 2; x += 2f)
        {
            for (float z = 2; z < terrainSize.z - 2; z += 2f)
            {
                Vector2 currentPos2D = new Vector2(x, z);

                if (startPoint != null && Vector2.Distance(currentPos2D, startPos2D) < startClearRadius) continue;
                if (goalPoint != null && Vector2.Distance(currentPos2D, goalPos2D) < goalClearRadius) continue;

                if (Random.value < terrainFamily.obstacleDensity)
                {
                    float y = terrain.SampleHeight(new Vector3(x + transform.position.x, 0, z + transform.position.z));
                    Vector3 spawnPos = new Vector3(x, y, z) + transform.position;

                    GameObject prefab = terrainFamily.obstaclePrefabs[Random.Range(0, terrainFamily.obstaclePrefabs.Length)];
                    GameObject obs = Instantiate(prefab, spawnPos, Quaternion.Euler(0, Random.Range(0, 360), 0));
                    obs.transform.parent = this.transform;
                    spawnedObstacles.Add(obs);
                }
            }
        }
    }

    void ClearObstacles()
    {
        foreach (var obj in spawnedObstacles)
        {
            if (obj != null) DestroyImmediate(obj);
        }
        spawnedObstacles.Clear();
    }
}