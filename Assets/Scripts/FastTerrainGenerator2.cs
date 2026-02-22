using UnityEngine;
using System.Collections.Generic;

public class FastTerrainGenerator2 : MonoBehaviour
{
    [Header("Referencias")]
    public TerrainFamily terrainFamily;
    public GameObject huskyRobot;
    public Transform goalPoint;

    [Header("Configuración TFG (OE2/OE4)")]
    public float safeRadius = 2.0f; // Proporcional a tu mapa de 20x20
    public float maxStepHeight = 0.4f; // Altura máxima de "escalón" que el rover puede subir

    private Terrain terrain;
    private TerrainData terrainData;
    private List<GameObject> spawnedObstacles = new List<GameObject>();

    void Awake()
    {
        terrain = GetComponent<Terrain>();
        // Creamos instancia única para no dañar el asset original
        terrainData = Instantiate(terrain.terrainData);
        terrain.terrainData = terrainData;
        if (TryGetComponent<TerrainCollider>(out var collider))
            collider.terrainData = terrainData;
    }

    void Start()
    {
        // Generación inicial para probar
        GenerateTerrain(42);
    }

    public void GenerateTerrain(int seed)
    {
        bool isPathValid = false;
        int attempt = 0;

        // Bucle de validación (OE2: Protocolo reproducible)
        while (!isPathValid && attempt < 10)
        {
            Random.InitState(seed + attempt);

            int res = terrainData.heightmapResolution;
            float[,] heights = new float[res, res];

            // 1. GENERACIÓN DE ALTURAS (Tu lógica original + Zonas Seguras Suaves)
            for (int i = 0; i < res; i++)
            {
                for (int j = 0; j < res; j++)
                {
                    // RUIDO BASE + MICRO-RUGOSIDAD (De tu script original)
                    float xCoord = (float)i / res * terrainFamily.noiseScale + (seed + attempt);
                    float yCoord = (float)j / res * terrainFamily.noiseScale + (seed + attempt);
                    float noise = Mathf.PerlinNoise(xCoord, yCoord) * terrainFamily.noiseAmplitude;
                    float detailNoise = Mathf.PerlinNoise(xCoord * 10, yCoord * 10) * (terrainFamily.noiseAmplitude * 0.2f);

                    // PENDIENTE (De tu script original)
                    float slope = (i / (float)res) * Mathf.Tan(terrainFamily.maxSlopeDegrees * Mathf.Deg2Rad) * 2;

                    float finalH = (noise + detailNoise + slope) / terrainData.size.y;

                    // APLICAR ZONAS SEGURAS (OE4) con SmoothStep para que no haya cortes
                    float distToSpawn = GetDistToPoint(i, j, huskyRobot.transform.position);
                    float distToGoal = GetDistToPoint(i, j, goalPoint.position);
                    float minSafeDist = Mathf.Min(distToSpawn, distToGoal);

                    if (minSafeDist < safeRadius)
                    {
                        float smoothFactor = Mathf.SmoothStep(0, 1, minSafeDist / safeRadius);
                        finalH *= smoothFactor;
                    }

                    heights[i, j] = Mathf.Clamp01(finalH);
                }
            }

            terrainData.SetHeights(0, 0, heights);

            // 2. VALIDACIÓN (Comprueba si el camino es físicamente posible)
            isPathValid = CheckPathViability();
            if (!isPathValid) attempt++;
        }

        // 3. OBSTÁCULOS Y POSICIONAMIENTO
        ClearObstacles();
        SpawnObstacles(seed);
        TeleportRobotToSurface();

        Debug.Log(isPathValid ? "Terreno válido generado." : "Aviso: No se encontró camino óptimo, se usó fallback.");
    }

    // --- MÉTODOS DE APOYO (LOS QUE TE FALTABAN) ---

    private float GetDistToPoint(int i, int j, Vector3 worldPos)
    {
        Vector3 localPos = worldPos - transform.position;
        float nodeX = (localPos.x / terrainData.size.x) * terrainData.heightmapResolution;
        float nodeZ = (localPos.z / terrainData.size.z) * terrainData.heightmapResolution;
        return Vector2.Distance(new Vector2(i, j), new Vector2(nodeX, nodeZ)) * (terrainData.size.x / terrainData.heightmapResolution);
    }

    private bool CheckPathViability()
    {
        int res = terrainData.heightmapResolution;
        bool[,] visited = new bool[res, res];
        Queue<Vector2Int> q = new Queue<Vector2Int>();

        Vector2Int start = WorldToGrid(huskyRobot.transform.position);
        Vector2Int end = WorldToGrid(goalPoint.position);

        q.Enqueue(start);
        visited[start.x, start.y] = true;

        while (q.Count > 0)
        {
            Vector2Int curr = q.Dequeue();
            if (Vector2Int.Distance(curr, end) < 4) return true;

            foreach (Vector2Int d in new Vector2Int[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
            {
                Vector2Int next = curr + d;
                if (next.x >= 0 && next.x < res && next.y >= 0 && next.y < res && !visited[next.x, next.y])
                {
                    float hCurr = terrainData.GetHeight(curr.x, curr.y);
                    float hNext = terrainData.GetHeight(next.x, next.y);

                    // Si el escalón es menor a maxStepHeight, el nodo es accesible
                    if (Mathf.Abs(hNext - hCurr) < maxStepHeight)
                    {
                        visited[next.x, next.y] = true;
                        q.Enqueue(next);
                    }
                }
            }
        }
        return false;
    }

    private Vector2Int WorldToGrid(Vector3 p)
    {
        Vector3 tP = p - transform.position;
        return new Vector2Int(
            Mathf.Clamp(Mathf.RoundToInt((tP.x / terrainData.size.x) * terrainData.heightmapResolution), 0, terrainData.heightmapResolution - 1),
            Mathf.Clamp(Mathf.RoundToInt((tP.z / terrainData.size.z) * terrainData.heightmapResolution), 0, terrainData.heightmapResolution - 1)
        );
    }

    void SpawnObstacles(int seed)
    {
        Random.InitState(seed);
        Vector3 terrainSize = terrainData.size;
        for (float x = 2; x < terrainSize.x - 2; x += 3f) // Espaciado mayor para 20x20
        {
            for (float z = 2; z < terrainSize.z - 2; z += 3f)
            {
                // No spawnear obstáculos en zonas seguras
                float distToSpawn = Vector3.Distance(new Vector3(x, 0, z) + transform.position, huskyRobot.transform.position);
                float distToGoal = Vector3.Distance(new Vector3(x, 0, z) + transform.position, goalPoint.position);

                if (distToSpawn > safeRadius && distToGoal > safeRadius && Random.value < terrainFamily.obstacleDensity)
                {
                    float y = terrain.SampleHeight(new Vector3(x + transform.position.x, 0, z + transform.position.z));
                    Vector3 spawnPos = new Vector3(x, y, z) + transform.position;

                    GameObject prefab = terrainFamily.obstaclePrefabs[Random.Range(0, terrainFamily.obstaclePrefabs.Length)];
                    GameObject obs = Instantiate(prefab, spawnPos, Quaternion.Euler(0, Random.Range(0, 360), 0), this.transform);
                    spawnedObstacles.Add(obs);
                }
            }
        }
    }

    void ClearObstacles()
    {
        foreach (var obj in spawnedObstacles) if (obj != null) DestroyImmediate(obj);
        spawnedObstacles.Clear();
    }

    public void TeleportRobotToSurface()
    {
        Vector3 pos = huskyRobot.transform.position;
        float groundHeight = terrain.SampleHeight(pos) + transform.position.y;
        huskyRobot.transform.position = new Vector3(pos.x, groundHeight + 0.5f, pos.z);

        ArticulationBody ab = huskyRobot.GetComponentInChildren<ArticulationBody>();
        if (ab != null)
        {
            ab.TeleportRoot(huskyRobot.transform.position, huskyRobot.transform.rotation);
            ab.linearVelocity = Vector3.zero;
            ab.angularVelocity = Vector3.zero;
        }
    }
}