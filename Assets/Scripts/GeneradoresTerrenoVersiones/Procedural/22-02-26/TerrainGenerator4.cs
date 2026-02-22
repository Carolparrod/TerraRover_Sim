using System.Collections.Generic;
using UnityEngine;

public class TerrainGenerator4 : MonoBehaviour
{
    public TerrainFam4 terrainFamily;
    private Terrain terrain;
    private TerrainData terrainData;

    // Lista para rastrear los obstáculos y borrarlos al regenerar
    private List<GameObject> spawnedObstacles = new List<GameObject>();
    public GameObject huskyRobot;

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
        Random.InitState(seed);
        ClearObstacles(); // 1. Limpiar lo anterior

        int res = terrainData.heightmapResolution;
        float[,] heights = new float[res, res];

        // Elevamos el "suelo cero" a un 30% de la altura total del terreno (Y=10 -> Base=3).
        // Esto permite que el ruido negativo excave "agujeros" sin salirse del límite inferior.
        float baseElevation = terrainData.size.y * 0.3f;

        // 2. Generar Alturas (Rugosidad + Colinas + Pendiente)
        for (int i = 0; i < res; i++)
        {
            for (int j = 0; j < res; j++)
            {
                /*// -- CAPA 1: MACRO-ESTRUCTURA (Colinas y Valles grandes) --
                float macroX = (float)i / res * terrainFamily.hillScale + seed;
                float macroY = (float)j / res * terrainFamily.hillScale + seed;
                // Restamos 0.5f para generar valores positivos (montañas) y negativos (valles)
                float macroNoise = (Mathf.PerlinNoise(macroX, macroY) - 0.5f) * terrainFamily.hillAmplitude;

                // -- CAPA 2: RUGOSIDAD MEDIA (Baches) --
                // +100 a la semilla para desfasar este ruido respecto al de las colinas
                float xCoord = (float)i / res * terrainFamily.noiseScale + (seed + 100);
                float yCoord = (float)j / res * terrainFamily.noiseScale + (seed + 100);
                float midNoise = Mathf.PerlinNoise(xCoord, yCoord) * terrainFamily.noiseAmplitude;

                // -- CAPA 3: MICRO-RUGOSIDAD (Detalle fino) --
                float microNoise = Mathf.PerlinNoise(xCoord * 10, yCoord * 10) * (terrainFamily.noiseAmplitude * 0.2f);

                // -- CAPA 4: PENDIENTE GENERAL --
                // Ajustada al tamaño real en X del terreno para que los grados sean exactos
                float slope = (i / (float)res) * terrainData.size.x * Mathf.Tan(terrainFamily.maxSlopeDegrees * Mathf.Deg2Rad);

                // Sumamos todas las capas a la elevación base
                float totalHeight = baseElevation + macroNoise + midNoise + microNoise + slope;

                // Normalizamos (0 a 1) respecto a la altura máxima del TerrainData
                // Nota: Unity Terrain lee el array como [y, x] (o [z, x] en coordenadas espaciales)
                heights[j, i] = Mathf.Clamp01(totalHeight / terrainData.size.y);*/
                // -- CAPA 1: MACRO-ESTRUCTURA (Colinas suaves) --
                float macroX = (float)i / res * terrainFamily.hillScale + seed;
                float macroY = (float)j / res * terrainFamily.hillScale + seed;
                float macroNoise = (Mathf.PerlinNoise(macroX, macroY) - 0.5f) * terrainFamily.hillAmplitude;

                // -- CAPA NUEVA: HOYOS PROMINENTES (Cráteres) --
                // Usamos una semilla distinta (+200) para que no coincidan con las montañas
                float holeX = (float)i / res * terrainFamily.holeScale + (seed + 200);
                float holeY = (float)j / res * terrainFamily.holeScale + (seed + 200);
                float rawHoleNoise = Mathf.PerlinNoise(holeX, holeY);
                // Aquí está la magia: elevamos el ruido a una potencia. 
                // Esto "aplana" los valores bajos y afila los altos, creando fosas localizadas.
                float holes = Mathf.Pow(rawHoleNoise, terrainFamily.holeSharpness) * terrainFamily.holeDepth;

                // -- CAPA 2 y 3: RUGOSIDAD (Tus baches actuales) --
                float xCoord = (float)i / res * terrainFamily.noiseScale + (seed + 100);
                float yCoord = (float)j / res * terrainFamily.noiseScale + (seed + 100);
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

        // 3. Generar Obstáculos salvajes respetando inicio/meta
        SpawnObstacles(seed);

        // 4. Asegurar que el robot no se caiga por el mapa
        TeleportRobotToSurface();
    }

    void SpawnObstacles(int seed)
    {
        Vector3 terrainSize = terrainData.size;

        // Posiciones 2D para calcular distancias fácilmente
        Vector2 startPos2D = startPoint != null ? new Vector2(startPoint.localPosition.x, startPoint.localPosition.z) : Vector2.zero;
        Vector2 goalPos2D = goalPoint != null ? new Vector2(goalPoint.localPosition.x, goalPoint.localPosition.z) : Vector2.zero;

        for (float x = 2; x < terrainSize.x - 2; x += 2f)
        {
            for (float z = 2; z < terrainSize.z - 2; z += 2f)
            {
                Vector2 currentPos2D = new Vector2(x, z);

                // ZONAS DE EXCLUSIÓN: Evitar rocas en el área de spawn del robot
                if (startPoint != null && Vector2.Distance(currentPos2D, startPos2D) < startClearRadius)
                    continue;

                // ZONAS DE EXCLUSIÓN: Evitar rocas tapando la meta
                if (goalPoint != null && Vector2.Distance(currentPos2D, goalPos2D) < goalClearRadius)
                    continue;

                // Spawn probabilístico basado en la densidad de la familia
                if (Random.value < terrainFamily.obstacleDensity)
                {
                    float y = terrain.SampleHeight(new Vector3(x + transform.position.x, 0, z + transform.position.z));
                    Vector3 spawnPos = new Vector3(x, y, z) + transform.position;

                    // Elegir prefab aleatorio y rotación aleatoria en Y
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

    void Start()
    {
        // Genera el terreno automáticamente al dar Play para verificar
        GenerateTerrain(42);
    }

    public void TeleportRobotToSurface()
    {
        Vector3 pos = huskyRobot.transform.position;
        // Calculamos la altura real del suelo en las coordenadas X,Z del robot
        float groundHeight = terrain.SampleHeight(pos) + transform.position.y;

        // Lo colocamos 1 metro por encima para que caiga limpiamente
        huskyRobot.transform.position = new Vector3(pos.x, groundHeight + 1.0f, pos.z);

        // Reseteamos las físicas del ArticulationBody
        ArticulationBody rootBody = huskyRobot.GetComponentInChildren<ArticulationBody>();
        if (rootBody != null)
        {
            rootBody.TeleportRoot(huskyRobot.transform.position, huskyRobot.transform.rotation);
            rootBody.linearVelocity = Vector3.zero;
            rootBody.angularVelocity = Vector3.zero;
        }
    }

    // Dibuja ayudas visuales en la ventana Scene
    private void OnDrawGizmos()
    {
        if (startPoint != null)
        {
            Gizmos.color = new Color(0, 1, 0, 0.3f); // Verde
            Gizmos.DrawSphere(startPoint.position, startClearRadius);
        }

        if (goalPoint != null)
        {
            Gizmos.color = new Color(1, 0, 0, 0.3f); // Rojo
            Gizmos.DrawSphere(goalPoint.position, goalClearRadius);
        }
    }
}