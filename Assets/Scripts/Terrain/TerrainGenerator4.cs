using System.Collections.Generic;
using UnityEngine;

public class TerrainGenerator4 : MonoBehaviour
{   
    public TerrainFam4 terrainFamily;
    private Terrain terrain;
    private TerrainData terrainData;

    // Lista para rastrear los obstáculos y borrarlos al regenerar
    private List<GameObject> spawnedObstacles = new List<GameObject>();

    [Header("Puntos de Navegación")]
    [Tooltip("Asigna aquí el GameObject vacío de Inicio")]
    public Transform startPoint;
    [Tooltip("Asigna aquí el GameObject vacío de Meta")]
    public Transform goalPoint;

    [Header("Zonas de Exclusión (Radios)")]
    [Tooltip("Radio libre de obstáculos alrededor del inicio")]
    public float startClearRadius = 3.0f;
    [Tooltip("Radio libre de obstáculos alrededor de la meta")]
    public float goalClearRadius = 3.0f;

    void Awake()
    {
        terrain = GetComponent<Terrain>();
        // Clonar el TerrainData para no sobreescribir el asset en el disco
        terrainData = Instantiate(terrain.terrainData);
        terrain.terrainData = terrainData;
        GetComponent<TerrainCollider>().terrainData = terrainData;
    }

    public void GenerateTerrain(int seed)
    {
        if (terrainFamily == null)
        {
            Debug.LogError("[TerrainGenerator4] ERROR: terrainFamily es NULL. Asigna un Terrain Family en el Inspector.");
            return;
        }
        Random.InitState(seed);
        ClearObstacles(); // 1. Limpiar lo anterior

        // CORRECCIÓN PERLIN NOISE: Offsets aleatorios para evitar la pérdida de precisión con semillas altas.
        // Usamos tres offsets distintos para que las colinas, hoyos y rugosidad no se alineen.
        float offsetMacro = Random.Range(0f, 9999f);
        float offsetHoles = Random.Range(0f, 9999f);
        float offsetMicro = Random.Range(0f, 9999f);

        int res = terrainData.heightmapResolution;
        float[,] heights = new float[res, res];

        // Elevamos el "suelo cero" a un 30% de la altura total del terreno.
        float baseElevation = terrainData.size.y * 0.3f;

        // 2. Generar Alturas
        for (int i = 0; i < res; i++)
        {
            for (int j = 0; j < res; j++)
            {
                // -- CAPA 1: MACRO-ESTRUCTURA (Colinas suaves) --
                float macroX = (float)i / res * terrainFamily.hillScale + offsetMacro;
                float macroY = (float)j / res * terrainFamily.hillScale + offsetMacro;
                float macroNoise = (Mathf.PerlinNoise(macroX, macroY) - 0.5f) * terrainFamily.hillAmplitude;

                // -- CAPA NUEVA: HOYOS PROMINENTES (Cráteres) --
                float holeX = (float)i / res * terrainFamily.holeScale + offsetHoles;
                float holeY = (float)j / res * terrainFamily.holeScale + offsetHoles;
                float rawHoleNoise = Mathf.PerlinNoise(holeX, holeY);
                float holes = Mathf.Pow(rawHoleNoise, terrainFamily.holeSharpness) * terrainFamily.holeDepth;

                // -- CAPA 2 y 3: RUGOSIDAD --
                float xCoord = (float)i / res * terrainFamily.noiseScale + offsetMicro;
                float yCoord = (float)j / res * terrainFamily.noiseScale + offsetMicro;
                float midNoise = Mathf.PerlinNoise(xCoord, yCoord) * terrainFamily.noiseAmplitude;
                float microNoise = Mathf.PerlinNoise(xCoord * 10, yCoord * 10) * (terrainFamily.noiseAmplitude * 0.2f);

                // -- CAPA 4: PENDIENTE --
                float slope = (i / (float)res) * terrainData.size.x * Mathf.Tan(terrainFamily.maxSlopeDegrees * Mathf.Deg2Rad);

                // IMPORTANTE: Sumamos las colinas, baches y pendiente... pero RESTAMOS los hoyos
                float totalHeight = baseElevation + macroNoise + midNoise + microNoise + slope - holes;

                heights[j, i] = Mathf.Clamp01(totalHeight / terrainData.size.y);
            }
        }

        // Aplicar las alturas al terreno
        terrainData.SetHeights(0, 0, heights);

        // 3. Generar Obstáculos
        SpawnObstacles(seed);
    }

    /*void SpawnObstacles(int seed)
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
    }*/
    /*void SpawnObstacles(int seed)
    {
        Vector3 terrainSize = terrainData.size;

        // Calculamos el centro exacto del terreno (donde nace el Husky)
        Vector2 centerPos2D = new Vector2(terrainSize.x / 2f, terrainSize.z / 2f);
        float radioSeguroHusky = 4.0f; // 4 metros libres de rocas en el centro

        for (float x = 2; x < terrainSize.x - 2; x += 2f)
        {
            for (float z = 2; z < terrainSize.z - 2; z += 2f)
            {
                Vector2 currentPos2D = new Vector2(x, z);

                // Si la roca que vamos a poner cae dentro de la zona segura del Husky, la saltamos
                if (Vector2.Distance(currentPos2D, centerPos2D) < radioSeguroHusky)
                {
                    continue;
                }

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
    void SpawnObstacles(int seed)
    {
        Vector3 terrainSize = terrainData.size;
        Vector2 centerPos2D = new Vector2(terrainSize.x / 2f, terrainSize.z / 2f);
        float radioSeguroHusky = 4.0f;

        // --- CHIVATOS AÑADIDOS ---
        int huecosTotales = 0;
        int rocasCreadas = 0;
        // -------------------------

        for (float x = 2; x < terrainSize.x - 2; x += 2f)
        {
            for (float z = 2; z < terrainSize.z - 2; z += 2f)
            {
                huecosTotales++; // Contamos cuántas veces se ejecuta el bucle

                Vector2 currentPos2D = new Vector2(x, z);

                if (Vector2.Distance(currentPos2D, centerPos2D) < radioSeguroHusky)
                {
                    continue;
                }

                if (Random.value < terrainFamily.obstacleDensity)
                {
                    float y = terrain.SampleHeight(new Vector3(x + transform.position.x, 0, z + transform.position.z));
                    Vector3 spawnPos = new Vector3(x, y, z) + transform.position;

                    GameObject prefab = terrainFamily.obstaclePrefabs[Random.Range(0, terrainFamily.obstaclePrefabs.Length)];
                    GameObject obs = Instantiate(prefab, spawnPos, Quaternion.Euler(0, Random.Range(0, 360), 0));

                    obs.transform.parent = this.transform;
                    spawnedObstacles.Add(obs);

                    rocasCreadas++; // Contamos las rocas reales
                }
            }
        }

        // --- LA CONFESIÓN FINAL ---
        //Debug.Log($"[REPORTE TERRENO] Tamaño real leído: {terrainSize.x}x{terrainSize.z} | Huecos revisados: {huecosTotales} | Densidad leída: {terrainFamily.obstacleDensity} | Rocas creadas: {rocasCreadas}");
    }
    /*
    void ClearObstacles()
    {
        foreach (var obj in spawnedObstacles)
        {
            if (obj != null) DestroyImmediate(obj);
        }
        spawnedObstacles.Clear();
    }*/ //este método causaba problemas al regenerar el terreno varias veces, porque DestroyImmediate no es seguro de usar en tiempo de ejecución. Lo cambiamos por Destroy, que marca los objetos para destrucción al final del frame, evitando errores.
    void ClearObstacles()
    {
        foreach (var obj in spawnedObstacles)
        {
            // Usamos Destroy en lugar de DestroyImmediate
            if (obj != null) Destroy(obj);
        }
        spawnedObstacles.Clear();
    }

    // ELIMINADOS: void Start() y void TeleportRobotToSurface() para no chocar con HuskyAgent.

    private void OnDrawGizmos()
    {
        if (startPoint != null)
        {
            Gizmos.color = new Color(0, 1, 0, 0.3f);
            Gizmos.DrawSphere(startPoint.position, startClearRadius);
        }

        if (goalPoint != null)
        {
            Gizmos.color = new Color(1, 0, 0, 0.3f);
            Gizmos.DrawSphere(goalPoint.position, goalClearRadius);
        }
    }
}