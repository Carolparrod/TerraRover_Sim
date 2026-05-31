using UnityEngine;
using System.Collections.Generic;

public class TerrainGenerator2 : MonoBehaviour
{
    public TerrainFamily terrainFamily;
    private Terrain terrain;
    private TerrainData terrainData;
    private List<GameObject> spawnedObstacles = new List<GameObject>();
    public GameObject huskyRobot;

    // Puntos de inicio y meta lógicos para el corredor seguro
    public Transform startPoint;
    public Transform goalPoint;
    //public float safeCorridorWidth = 3.0f; // Ancho libre de obstáculos
    public float safeCorridorWidth = 1.5f;

    //CAMBIO 
    /// [Header("Zonas de Exclusión")]
    public float startClearRadius = 3.0f;
    public float goalClearRadius = 3.0f;
    /// </summary>
    void Awake()
    {
        terrain = GetComponent<Terrain>();
        // Clonamos el TerrainData para no modificar el asset original en el editor
        terrainData = Instantiate(terrain.terrainData);
        terrain.terrainData = terrainData;
        GetComponent<TerrainCollider>().terrainData = terrainData;
    }

    public void GenerateTerrain(int seed)
    {
        Random.InitState(seed);
        ClearObstacles();

        int res = terrainData.heightmapResolution;
        float[,] heights = new float[res, res];

        // 1. Generar Alturas
        for (int i = 0; i < res; i++)
        {
            for (int j = 0; j < res; j++)
            {
                // Coordenadas del mundo para este punto del grid
                float worldX = ((float)i / res) * terrainData.size.x;
                float worldZ = ((float)j / res) * terrainData.size.z;

                // Calcular factor del corredor seguro (0 = dentro del centro del pasillo, 1 = fuera)
                float corridorFactor = GetCorridorFactor(worldX, worldZ);

                float xCoord = (float)i / res * terrainFamily.noiseScale + seed;
                float yCoord = (float)j / res * terrainFamily.noiseScale + seed;

                // Atenuamos el ruido base dentro del corredor seguro
                float noise = Mathf.PerlinNoise(xCoord, yCoord) * terrainFamily.noiseAmplitude * corridorFactor;
                float detailNoise = Mathf.PerlinNoise(xCoord * 10, yCoord * 10) * (terrainFamily.noiseAmplitude * 0.2f) * corridorFactor;

                //float slope = (i / (float)res) * Mathf.Tan(terrainFamily.maxSlopeDegrees * Mathf.Deg2Rad) * 2;
                float slope = (i / (float)res) * terrainData.size.x * Mathf.Tan(terrainFamily.maxSlopeDegrees * Mathf.Deg2Rad);

                heights[j, i] = Mathf.Clamp01((noise + detailNoise + slope) / terrainData.size.y);
            }
        }
        terrainData.SetHeights(0, 0, heights);

        // 2. Generar Obstáculos (respetando el pasillo)
        SpawnObstacles(seed);

        // 3. Posicionar el robot
        TeleportRobotToSurface();
    }

    /*void SpawnObstacles(int seed)
    {
        Vector3 terrainSize = terrainData.size;
        for (float x = 2; x < terrainSize.x - 2; x += 2f)
        {
            for (float z = 2; z < terrainSize.z - 2; z += 2f)
            {
                // Comprobamos si la posición está dentro del corredor seguro
                if (GetCorridorFactor(x, z) < 0.9f)
                    continue; // Saltar generación aquí para dejar el camino libre

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
    }*/
    //CAMBIO
    void SpawnObstacles(int seed)
    {
        Vector3 terrainSize = terrainData.size;

        // Obtenemos las posiciones locales X y Z de inicio y meta para calcular distancias
        Vector2 startPos2D = startPoint != null ? new Vector2(startPoint.localPosition.x, startPoint.localPosition.z) : Vector2.zero;
        Vector2 goalPos2D = goalPoint != null ? new Vector2(goalPoint.localPosition.x, goalPoint.localPosition.z) : Vector2.zero;

        for (float x = 2; x < terrainSize.x - 2; x += 2f)
        {
            for (float z = 2; z < terrainSize.z - 2; z += 2f)
            {
                Vector2 currentPos2D = new Vector2(x, z);

                // 1. ZONAS DE EXCLUSIÓN: Comprobar si estamos muy cerca del inicio
                if (startPoint != null && Vector2.Distance(currentPos2D, startPos2D) < startClearRadius)
                    continue; // Saltar y no generar nada aquí

                // 2. ZONAS DE EXCLUSIÓN: Comprobar si estamos muy cerca de la meta
                if (goalPoint != null && Vector2.Distance(currentPos2D, goalPos2D) < goalClearRadius)
                    continue; // Saltar y no generar nada aquí

                // 3. CORREDOR SEGURO: (Si estás usando la función GetCorridorFactor)
                // if (GetCorridorFactor(x, z) < 0.9f) continue; 

                // 4. GENERACIÓN DE LA ROCA (Si pasamos todos los filtros)
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

    // Calcula si un punto está cerca de la línea recta entre el inicio y la meta
    float GetCorridorFactor(float x, float z)
    {
        if (startPoint == null || goalPoint == null) return 1f;

        Vector2 p = new Vector2(x, z);
        Vector2 v = new Vector2(startPoint.localPosition.x, startPoint.localPosition.z);
        Vector2 w = new Vector2(goalPoint.localPosition.x, goalPoint.localPosition.z);

        // Distancia del punto a un segmento de línea
        float l2 = (w - v).sqrMagnitude;
        if (l2 == 0.0) return Mathf.Clamp01(Vector2.Distance(p, v) / safeCorridorWidth);

        float t = Mathf.Max(0, Mathf.Min(1, Vector2.Dot(p - v, w - v) / l2));
        Vector2 projection = v + t * (w - v);
        float distance = Vector2.Distance(p, projection);

        // Devuelve 0 en el centro del pasillo, y transiciona a 1 hacia los bordes
        return Mathf.Clamp01(distance / safeCorridorWidth);
    }

    void ClearObstacles()
    {
        foreach (var obj in spawnedObstacles)
        {
            if (obj != null) DestroyImmediate(obj);
        }
        spawnedObstacles.Clear();
    }

    void Start()
    {
        GenerateTerrain(42);
    }

    public void TeleportRobotToSurface()
    {
        Vector3 pos = huskyRobot.transform.position;
        float groundHeight = terrain.SampleHeight(pos) + transform.position.y;
        huskyRobot.transform.position = new Vector3(pos.x, groundHeight + 1.0f, pos.z);

        ArticulationBody rootBody = huskyRobot.GetComponentInChildren<ArticulationBody>();
        if (rootBody != null)
        {
            rootBody.TeleportRoot(huskyRobot.transform.position, huskyRobot.transform.rotation);
            rootBody.linearVelocity = Vector3.zero;
            rootBody.angularVelocity = Vector3.zero;
        }
    }

    //CAMBIO
    // Dibuja ayudas visuales en la vista Scene de Unity
    private void OnDrawGizmos()
    {
        // Dibujar zona segura de Inicio (Verde)
        if (startPoint != null)
        {
            Gizmos.color = new Color(0, 1, 0, 0.3f); // Verde semitransparente
            Gizmos.DrawSphere(startPoint.position, startClearRadius);
        }

        // Dibujar zona segura de Meta (Rojo)
        if (goalPoint != null)
        {
            Gizmos.color = new Color(1, 0, 0, 0.3f); // Rojo semitransparente
            Gizmos.DrawSphere(goalPoint.position, goalClearRadius);
        }
    }
}