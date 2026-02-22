using System.Collections.Generic;
using Unity.AppUI.UI;
using UnityEngine;
using UnityEngine.UIElements;

public class TerrainGeneratorSafe : MonoBehaviour
{
    public TerrainFamily terrainFamily;
    private Terrain terrain;
    private TerrainData terrainData;

    // Lista para rastrear los obst�culos y borrarlos al regenerar
    private List<GameObject> spawnedObstacles = new List<GameObject>();

    public GameObject huskyRobot;

    void Awake()
    {
        terrain = GetComponent<Terrain>();
        terrainData = Instantiate(terrain.terrainData);
        terrain.terrainData = terrainData;
        GetComponent<TerrainCollider>().terrainData = terrainData;
        //terrainData = terrain.terrainData;
    }

    public void GenerateTerrain(int seed)
    {
        Random.InitState(seed);
        ClearObstacles(); // Paso 1: Limpiar lo anterior

        int res = terrainData.heightmapResolution;
        float[,] heights = new float[res, res];

        // Paso 2: Generar Alturas (Rugosidad + Pendiente)
        for (int i = 0; i < res; i++)
        {
            for (int j = 0; j < res; j++)
            {
                // 1. RUIDO BASE (Baches grandes / Lomas)
                float xCoord = (float)i / res * terrainFamily.noiseScale + seed;
                float yCoord = (float)j / res * terrainFamily.noiseScale + seed;
                float noise = Mathf.PerlinNoise(xCoord, yCoord) * terrainFamily.noiseAmplitude;


                // 2. MICRO-RUGOSIDAD (Pequeños baches para vibración)
                // Usamos una escala mucho más alta (frecuencia) para el detalle fino
                float detailNoise = Mathf.PerlinNoise(xCoord * 10, yCoord * 10) * (terrainFamily.noiseAmplitude * 0.2f);

                // 3. PENDIENTE (Opcional: puedes comentarla si quieres suelo plano con baches)
                // Si quieres altibajos, pon maxSlopeDegrees en 0 o un valor muy bajo (2-5)
                float slope = (i / (float)res) * Mathf.Tan(terrainFamily.maxSlopeDegrees * Mathf.Deg2Rad) * 2;
                //float slope = (i / (float)res) * Mathf.Tan(terrainFamily.maxSlopeDegrees * Mathf.Deg2Rad) * 5;
                float finalHeight = (noise + detailNoise + slope); // Altura en metros antes de normalizar
                //Calculamos la posición del nodo actual en metros para compararla con el Spawn
                Vector3 currentPos = new Vector3(i, 0, j) * (terrainData.size.x / res);

                // Comparamos con la posición local del robot (ajusta si tu robot no sale del 0,0)
                float distToSpawn = Vector3.Distance(currentPos, Vector3.zero);

                if (distToSpawn < 2.0f)
                {
                    // Aplica el suavizado (OE4)
                    float smooth = Mathf.SmoothStep(0, 1, distToSpawn / 2.0f);
                    finalHeight *= smooth;
                }

                // Sumamos todo
                //heights[i, j] = Mathf.Clamp01((noise + detailNoise + slope) / terrainData.size.y);
                //heights[i, j] = Mathf.Clamp01((noise + slope) / terrainData.size.y);
                heights[i, j] = Mathf.Clamp01(finalHeight / terrainData.size.y);


            }
        }
        terrainData.SetHeights(0, 0, heights);

        // Paso 3: Generar Obst�culos
        SpawnObstacles(seed);
    }

    void SpawnObstacles(int seed)
    {
        Vector3 terrainSize = terrainData.size;
        // Iteramos por el terreno saltando espacios para no saturar
        for (float x = 2; x < terrainSize.x - 2; x += 2f)
        {
            for (float z = 2; z < terrainSize.z - 2; z += 2f)
            {
                if (Random.value < terrainFamily.obstacleDensity)
                {
                    float y = terrain.SampleHeight(new Vector3(x + transform.position.x, 0, z + transform.position.z));
                    Vector3 spawnPos = new Vector3(x, y, z) + transform.position;

                    // Elegir una roca al azar de la lista
                    GameObject prefab = terrainFamily.obstaclePrefabs[Random.Range(0, terrainFamily.obstaclePrefabs.Length)];

                    GameObject obs = Instantiate(prefab, spawnPos, Quaternion.Euler(0, Random.Range(0, 360), 0));
                    obs.transform.parent = this.transform; // Los hacemos hijos para orden
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
        // Genera un terreno con la semilla 42 al empezar ////COMPROBACION DE QUE SE GENERE AL INICIAR
        GenerateTerrain(42);
        TeleportRobotToSurface();
    }

    public void TeleportRobotToSurface()
    {
        // 1. Obtener la posición actual X y Z
        Vector3 pos = huskyRobot.transform.position;

        // 2. Calcular la altura exacta del terreno en ese punto
        // Sumamos la posición Y del terreno por si no está en el origen (0,0,0)
        float groundHeight = terrain.SampleHeight(pos) + transform.position.y;

        // 3. Teletransportar al Husky (le damos 1 metro de margen para que caiga suavemente)
        huskyRobot.transform.position = new Vector3(pos.x, groundHeight + 1.0f, pos.z);

        // 4. Resetear la física del ArticulationBody para evitar comportamientos erráticos
        ArticulationBody rootBody = huskyRobot.GetComponentInChildren<ArticulationBody>();
        if (rootBody != null)
        {
            rootBody.TeleportRoot(huskyRobot.transform.position, huskyRobot.transform.rotation);
            rootBody.linearVelocity = Vector3.zero;
            rootBody.angularVelocity = Vector3.zero;
        }
    }
}