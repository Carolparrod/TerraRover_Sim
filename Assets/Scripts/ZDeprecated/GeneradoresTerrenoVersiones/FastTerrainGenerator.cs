using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Terrain))]
public class FastTerrainGenerator : MonoBehaviour
{
    [Header("Configuración de Semilla")]
    public int seed = 42;

    [Header("Parámetros del Terreno (OE2)")]
    public float noiseScale = 0.05f;
    public float noiseAmplitude = 8f;
    public float maxSlopeDegrees = 25f; // Máxima pendiente permitida para validar el camino

    [Header("Zonas de Seguridad (OE4)")]
    public Transform spawnPoint;
    public Transform goalPoint;
    public float safeRadius = 4f;

    [Header("Referencias")]
    public GameObject robot;

    private Terrain terrain;
    private TerrainData terrainData;

    void Awake()
    {
        terrain = GetComponent<Terrain>();
        // Creamos una instancia única para no sobrescribir el asset original [cite: 17]
        terrainData = Instantiate(terrain.terrainData);
        terrain.terrainData = terrainData;
        if (TryGetComponent<TerrainCollider>(out var collider))
        {
            collider.terrainData = terrainData;
        }
    }

    void Start()
    {
        // Generación inicial al arrancar la simulación
        GenerateTerrain(seed);
    }

    /// <summary>
    /// Genera un terreno válido, aleatoriza posiciones y teletransporta al robot.
    /// </summary>
    /*public void GenerateTerrain(int newSeed)
    {
        seed = newSeed;
        Random.InitState(seed);

        bool isPathValid = false;
        int attempts = 0;

        while (!isPathValid && attempts < 50)
        {
            // 1. Aleatorizar posiciones de inicio y fin (Generalización) [cite: 3, 5]
            RandomizePoints();

            // 2. Generar relieve basado en Perlin Noise [cite: 18]
            ApplyPerlinHeights(seed + attempts);

            // 3. Aplanar zonas de Spawn y Goal (Zonas Seguras) [cite: 9]
            ApplyFlattening();

            terrainData.SyncHeightmap();

            // 4. Validar si el Rover puede cruzar (OE2/OE8) [cite: 7, 13]
            isPathValid = CheckPathViability();

            if (!isPathValid) attempts++;
        }

        Debug.Log(isPathValid ? $"Terreno validado en intento {attempts} (Seed: {seed})" : "No se encontró camino válido.");
        TeleportRobot();
    }*/
    public void GenerateTerrain(int newSeed)
    {
        seed = newSeed;
        Random.InitState(seed);

        bool isPathValid = false;
        int attempts = 0;
        int maxSafetyAttempts = 15; // <--- ESTO EVITA EL PETAZO

        while (!isPathValid && attempts < maxSafetyAttempts)
        {
            RandomizePoints();
            ApplyPerlinHeights(seed + attempts);
            ApplyFlattening();

            terrainData.SyncHeightmap();

            isPathValid = CheckPathViability();
            if (!isPathValid) attempts++;
        }

        if (!isPathValid)
        {
            // Si después de 15 intentos no hay camino, forzamos un terreno plano
            // para que el TFG pueda seguir su curso sin colgarse (OE11).
            Debug.LogWarning("Semilla difícil detectada. Generando terreno plano de seguridad.");
            FlattenEmergency();
        }

        TeleportRobot();
    }

    private void RandomizePoints()
    {
        float margin = 6f;
        float width = terrainData.size.x;
        float length = terrainData.size.z;

        // Coloca el Spawn en el lado izquierdo y el Goal en el derecho [cite: 7]
        spawnPoint.position = new Vector3(Random.Range(margin, width * 0.15f), 0, Random.Range(margin, length - margin)) + transform.position;
        goalPoint.position = new Vector3(Random.Range(width * 0.85f, width - margin), 0, Random.Range(margin, length - margin)) + transform.position;
    }

    private void ApplyPerlinHeights(int s)
    {
        int res = terrainData.heightmapResolution;
        float[,] heights = new float[res, res];

        for (int i = 0; i < res; i++)
        {
            for (int j = 0; j < res; j++)
            {
                float xCoord = (float)i / res * (noiseScale * 100) + s;
                float yCoord = (float)j / res * (noiseScale * 100) + s;
                float noise = Mathf.PerlinNoise(xCoord, yCoord) * noiseAmplitude;
                heights[i, j] = noise / terrainData.size.y;
            }
        }
        terrainData.SetHeights(0, 0, heights);
    }

    private void ApplyFlattening()
    {
        FlattenArea(spawnPoint.position, safeRadius);
        FlattenArea(goalPoint.position, safeRadius);
    }

    /*private void FlattenArea(Vector3 worldPos, float radius)
    {
        int res = terrainData.heightmapResolution;
        Vector3 terrainPos = worldPos - transform.position;

        int centerX = Mathf.RoundToInt((terrainPos.x / terrainData.size.x) * res);
        int centerZ = Mathf.RoundToInt((terrainPos.z / terrainData.size.z) * res);
        int radiusNodes = Mathf.RoundToInt((radius / terrainData.size.x) * res);

        for (int i = centerX - radiusNodes; i <= centerX + radiusNodes; i++)
        {
            for (int j = centerZ - radiusNodes; j <= centerZ + radiusNodes; j++)
            {
                if (i >= 0 && i < res && j >= 0 && j < res)
                {
                    terrainData.SetHeights(i, j, new float[,] { { 0 } });
                }
            }
        }
    }*/
    private void FlattenArea(Vector3 worldPos, float radius)
    {
        int res = terrainData.heightmapResolution;
        Vector3 terrainPos = worldPos - transform.position;

        int centerX = Mathf.RoundToInt((terrainPos.x / terrainData.size.x) * res);
        int centerZ = Mathf.RoundToInt((terrainPos.z / terrainData.size.z) * res);
        int radiusNodes = Mathf.RoundToInt((radius / terrainData.size.x) * res);

        // JUSTIFICACIÓN TFG: Se aplica un desvanecimiento (falloff) para evitar
        // discontinuidades físicas que el ArticulationBody no pueda procesar.
        for (int i = centerX - radiusNodes; i <= centerX + radiusNodes; i++)
        {
            for (int j = centerZ - radiusNodes; j <= centerZ + radiusNodes; j++)
            {
                if (i >= 0 && i < res && j >= 0 && j < res)
                {
                    float distance = Vector2.Distance(new Vector2(i, j), new Vector2(centerX, centerZ));
                    float t = Mathf.Clamp01(distance / radiusNodes);

                    // t=0 es el centro (plano), t=1 es el borde (terreno original)
                    float currentHeight = terrainData.GetHeight(i, j) / terrainData.size.y;
                    float smoothedHeight = Mathf.Lerp(0, currentHeight, t);

                    terrainData.SetHeights(i, j, new float[,] { { smoothedHeight } });
                }
            }
        }
    }

    private bool CheckPathViability()
    {
        // Algoritmo de inundación (Flood Fill) para validar pendientes [cite: 7, 13]
        int res = terrainData.heightmapResolution;
        bool[,] visited = new bool[res, res];
        Queue<Vector2Int> queue = new Queue<Vector2Int>();

        Vector2Int startNode = WorldToGrid(spawnPoint.position);
        Vector2Int endNode = WorldToGrid(goalPoint.position);

        queue.Enqueue(startNode);
        visited[startNode.x, startNode.y] = true;

        while (queue.Count > 0)
        {
            Vector2Int curr = queue.Dequeue();
            if (Vector2Int.Distance(curr, endNode) < 4) return true;

            foreach (Vector2Int dir in new Vector2Int[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
            {
                Vector2Int neighbor = curr + dir;
                if (neighbor.x >= 0 && neighbor.x < res && neighbor.y >= 0 && neighbor.y < res && !visited[neighbor.x, neighbor.y])
                {
                    float h1 = terrainData.GetHeight(curr.x, curr.y);
                    float h2 = terrainData.GetHeight(neighbor.x, neighbor.y);
                    float dist = terrainData.size.x / res;
                    // JUSTIFICACIÓN: Calculamos el ángulo de la pendiente entre nodos contiguos [cite: 7, 12]
                    float slope = Mathf.Abs(Mathf.Atan((h2 - h1) / dist) * Mathf.Rad2Deg);

                    if (slope <= maxSlopeDegrees)
                    {
                        visited[neighbor.x, neighbor.y] = true;
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }
        return false;
    }

    private Vector2Int WorldToGrid(Vector3 worldPos)
    {
        Vector3 temp = worldPos - transform.position;
        return new Vector2Int(
            Mathf.Clamp(Mathf.RoundToInt((temp.x / terrainData.size.x) * terrainData.heightmapResolution), 0, terrainData.heightmapResolution - 1),
            Mathf.Clamp(Mathf.RoundToInt((temp.z / terrainData.size.z) * terrainData.heightmapResolution), 0, terrainData.heightmapResolution - 1)
        );
    }

    public void TeleportRobot()
    {
        float groundHeight = terrain.SampleHeight(spawnPoint.position) + transform.position.y;
        robot.transform.position = new Vector3(spawnPoint.position.x, groundHeight + 0.8f, spawnPoint.position.z);

        // Sincronización con ArticulationBody para evitar errores de física [cite: 6, 21]
        ArticulationBody ab = robot.GetComponentInChildren<ArticulationBody>();
        if (ab != null)
        {
            ab.TeleportRoot(robot.transform.position, Quaternion.identity);
            ab.linearVelocity = Vector3.zero;
            ab.angularVelocity = Vector3.zero;
        }
    }

    private void FlattenEmergency()
    {
        int res = terrainData.heightmapResolution;
        terrainData.SetHeights(0, 0, new float[res, res]);
    }
}