using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// OE7 - Controlador Heurístico Baseline para el Husky.
/// Implementa una política determinista simple basada en tres reglas:
///   1. Rumbo: gira proporcionalmente al ángulo de error hacia la meta.
///   2. Velocidad: la reduce en función de la pendiente del terreno.
///   3. Evasión: detecta obstáculos con SphereCasts replicando la geometría
///      exacta del RayPerceptionSensor (mismos parámetros) y aplica corrección de rumbo.
/// Usa exactamente la misma interfaz de ruedas (ArticulationBody) y las mismas
/// condiciones de episodio que HuskyAgent2, para que la comparación sea justa.
/// </summary>
public class HuskyHeuristic : MonoBehaviour
{
    // -----------------------------------------------------------------------
    // REFERENCIAS (mismas que HuskyAgent2)
    // -----------------------------------------------------------------------
    [Header("Referencias")]
    public TerrainGenerator4 terrainGenerator;
    public ArticulationBody baseLink;
    public Transform target;
    public Terrain groundTerrain;

    // -----------------------------------------------------------------------
    // CONFIGURACIÓN DE RUEDAS (idéntica a HuskyAgent2)
    // -----------------------------------------------------------------------
    [Header("Configuración de Ruedas")]
    public ArticulationBody[] leftWheels;
    public ArticulationBody[] rightWheels;
    public float trackWidth    = 0.55f;
    public float wheelRadius   = 0.165f;
    public float maxLinearSpeed  = 1.5f;
    public float maxAngularSpeed = 2f;

    // -----------------------------------------------------------------------
    // PARÁMETROS HEURÍSTICOS
    // -----------------------------------------------------------------------
    [Header("Parámetros de Navegación Heurística")]
    [Tooltip("Ángulo (grados) a partir del cual se aplica corrección de rumbo máxima")]
    public float maxSteeringAngle = 45f;

    [Tooltip("Giro mínimo garantizado cuando hay cualquier error de rumbo (evita giro tardío)")]
    [Range(0f, 0.5f)] public float minTurnResponse = 0.15f;

    [Tooltip("Ángulo (grados) a partir del cual el rover frena y gira en el sitio antes de avanzar")]
    public float inPlaceRotationThreshold = 100f;

    [Tooltip("Ángulo (grados) por debajo del cual el rover sale del modo reorientación (histéresis)")]
    public float inPlaceRotationExit = 70f;

    // Estado interno del modo reorientación
    private bool inPlaceRotating = false;

    [Tooltip("Umbral de tilt (1 - transform.up.y) a partir del cual se reduce velocidad")]
    public float slopeThreshold = 0.1f;

    [Tooltip("Factor mínimo de velocidad en pendiente máxima (0=para, 1=no reduce)")]
    [Range(0f, 1f)] public float minSpeedOnSlope = 0.4f;

    [Header("Evasión de Obstáculos (SphereCast — réplica exacta del LiDAR)")]
    [Tooltip("Igual que Ray Length del RayPerceptionSensor")]
    public float rayLength = 8f;
    [Tooltip("Igual que Sphere Cast Radius del RayPerceptionSensor")]
    public float sphereRadius = 0.25f;
    [Tooltip("Igual que Rays Per Direction del RayPerceptionSensor")]
    public int raysPerDirection = 7;
    [Tooltip("Igual que Max Ray Degrees del RayPerceptionSensor")]
    public float maxRayDegrees = 70f;

    [Tooltip("Peso de la corrección de evasión sobre el giro de rumbo")]
    [Range(0f, 3f)] public float evasionWeight = 2.0f;

    [Tooltip("HitFraction por debajo del cual se considera peligro (0=nariz, 1=lejos)")]
    [Range(0f, 1f)] public float dangerFraction = 0.4f;

    // -----------------------------------------------------------------------
    // CONDICIONES DE EPISODIO (mismas que HuskyAgent2)
    // -----------------------------------------------------------------------
    [Header("Condiciones de Episodio")]
    public float successDistance = 2.0f;
    public int   envSeed      = 42;
    public bool  useFixedSeed = false;

    [Tooltip("Activa el uso de la lista de semillas para evaluación controlada (OE8).")]
    public bool  useSeedList = false;
    [Tooltip("Lista de semillas. Rellenar con el botón derecho → Generar lista de semillas (OE8).")]
    public int[] seedList = new int[0];
    private int  seedIndex = 0;

    [Tooltip("Número de semillas a generar automáticamente")]
    public int numSeedsToGenerate = 100;
    [Tooltip("Semilla maestra para generar la lista. Usar el MISMO valor en HuskyAgent2 y HuskyHeuristic para garantizar terrenos idénticos.")]
    public int masterSeed = 12345;

    [ContextMenu("Generar lista de semillas (OE8)")]
    private void GenerarListaSemillas()
    {
        var rng = new System.Random(masterSeed);
        seedList = new int[numSeedsToGenerate];
        for (int i = 0; i < numSeedsToGenerate; i++)
            seedList[i] = rng.Next(1, 1000000);
        Debug.Log($"[HuskyHeuristic] Lista de {numSeedsToGenerate} semillas generada con masterSeed={masterSeed}.");
    }

    [Header("Anti-Atasco")]
    public float stuckRadiusThreshold = 0.05f;
    public int   stuckCheckInterval   = 100;
    public int   maxStuckPermitido    = 5;
    public int   maxSpinPermitido     = 3;

    [Header("Límites del Terreno")]
    public float terrainWidthX  = 50f;
    public float terrainLengthZ = 50f;

    // -----------------------------------------------------------------------
    // MÉTRICAS (OE8 / OE11)
    // -----------------------------------------------------------------------
    [Header("Registro de Métricas (OE8/OE11)")]
    [Tooltip("Activa el guardado de métricas en CSV para análisis estadístico")]
    public bool   guardarMetricas = true;
    public string csvFileName     = "HuskyHeuristic_metricas.csv";

    // -----------------------------------------------------------------------
    // ESTADO INTERNO
    // -----------------------------------------------------------------------
    private int   episodeCount   = 0;
    private int   stepCount      = 0;
    private int   totalSuccesses = 0;
    private int   totalFailures  = 0;
    private float episodeStartTime;
    private float totalEnergyThisEpisode = 0f;
    private float previousDistanceToTarget;

    // Anti-atasco
    private int        checkTimer   = 0;
    private int        stuckCounter = 0;
    private int        spinCounter  = 0;
    private Vector3    lastPosition;
    private Quaternion lastRotation;

    // Flash visual
    private Color    originalGroundColor;
    private Coroutine flashCoroutine;

    // CSV
    private StreamWriter csvWriter;

    // -----------------------------------------------------------------------
    // INICIALIZACIÓN
    // -----------------------------------------------------------------------
    void Start()
    {
        ConfigurarMotores(leftWheels);
        ConfigurarMotores(rightWheels);

        if (groundTerrain != null && groundTerrain.materialTemplate != null)
        {
            groundTerrain.materialTemplate = new Material(groundTerrain.materialTemplate);
            originalGroundColor = groundTerrain.materialTemplate.color;
        }

        if (guardarMetricas)
            InicializarCSV();

        IniciarEpisodio();
    }

    void OnDestroy()
    {
        if (csvWriter != null)
        {
            csvWriter.Flush();
            csvWriter.Close();
        }
    }

    // -----------------------------------------------------------------------
    // BUCLE PRINCIPAL
    // -----------------------------------------------------------------------
    void FixedUpdate()
    {
        stepCount++;
        EjecutarHeuristica();
        ComprobarEstadosTerminales();
    }

    // -----------------------------------------------------------------------
    // LÓGICA HEURÍSTICA PRINCIPAL
    // -----------------------------------------------------------------------
    private void EjecutarHeuristica()
    {
        // --- 1. RUMBO: ángulo de error hacia la meta ---
        Vector3 dirToTarget = target.position - transform.position;
        dirToTarget.y = 0f; // Ignoramos diferencia de altura para el rumbo
        float angleError = Vector3.SignedAngle(transform.forward, dirToTarget.normalized, Vector3.up);

        // --- 2. VELOCIDAD BASE según pendiente ---
        float tilt = 1.0f - transform.up.y;
        float speedFactor = 1.0f;
        if (tilt > slopeThreshold)
        {
            float normalizedTilt = Mathf.Clamp01((tilt - slopeThreshold) / (0.5f - slopeThreshold));
            speedFactor = Mathf.Lerp(1.0f, minSpeedOnSlope, normalizedTilt);
        }
        float moveAction = speedFactor;

        // Acción de giro con curva no lineal y mínimo garantizado
        float normalizedError = Mathf.Clamp(angleError / maxSteeringAngle, -1f, 1f);
        float turnMagnitude   = Mathf.Lerp(minTurnResponse, 1f, Mathf.Abs(normalizedError));
        float turnAction      = Mathf.Sign(normalizedError) * turnMagnitude;

        // REORIENTACIÓN EN EL SITIO con histéresis:
        // Entra en modo reorientación si el error supera inPlaceRotationThreshold.
        // Sale solo cuando baja de inPlaceRotationExit. Evita el efecto de trompicones.
        float absError = Mathf.Abs(angleError);
        if (absError > inPlaceRotationThreshold)
            inPlaceRotating = true;
        else if (absError < inPlaceRotationExit)
            inPlaceRotating = false;

        if (inPlaceRotating)
        {
            moveAction = 0f;
            turnAction = Mathf.Sign(angleError);
        }

        // --- 3. EVASIÓN DE OBSTÁCULOS ---
        ObtenerRayos(out float evasionTurn, out float frontalThreat, out float bestFreeAngle);
        turnAction = Mathf.Clamp(turnAction + evasionTurn * evasionWeight, -1f, 1f);

        if (frontalThreat > 0f)
        {
            // Freno cuadrático: suave cuando lejos, agresivo cuando muy cerca
            moveAction *= 1.0f - (frontalThreat * frontalThreat) * 0.8f;

            // ANTI-PARÁLISIS: cuando la evasión es ambigua (múltiples rocas simétricas
            // o roca justo al frente), girar hacia el hueco más despejado detectado
            if (frontalThreat > 0.5f && Mathf.Abs(evasionTurn) < 0.2f)
            {
                float dirHueco = Mathf.Clamp(bestFreeAngle / maxRayDegrees, -1f, 1f);
                turnAction = Mathf.Abs(dirHueco) > 0.1f ? dirHueco : (angleError >= 0f ? 1f : -1f);
                moveAction = 0f;
            }
        }

        // --- 4. APLICAR ACCIONES A LAS RUEDAS ---
        float desiredLinear  = moveAction  * maxLinearSpeed;
        float desiredAngular = turnAction  * maxAngularSpeed;

        float leftVel  = desiredLinear + (desiredAngular * (trackWidth / 2f));
        float rightVel = desiredLinear - (desiredAngular * (trackWidth / 2f));

        AplicarVelocidadAngular(leftWheels,  leftVel);
        AplicarVelocidadAngular(rightWheels, rightVel);

        // Acumular energía (para métrica OE8: coste energético)
        totalEnergyThisEpisode += Mathf.Abs(leftVel) + Mathf.Abs(rightVel);
    }

    // -----------------------------------------------------------------------
    // EVASIÓN: SphereCasts replicando la geometría exacta del RayPerceptionSensor.
    // Rays Per Direction = 7, Max Ray Degrees = 70, Alternating Ray Order = true:
    //   Índice 0          → centro (0°)
    //   Índices impares   → derecha  (+10°, +20°, …, +70°)
    //   Índices pares > 0 → izquierda (−10°, −20°, …, −70°)
    // -----------------------------------------------------------------------
    /// <summary>
    /// SphereCasts replicando la geometría del RayPerceptionSensor.
    /// Usa suma ponderada (no Max) para acumular la repulsión de cada lado,
    /// lo que maneja correctamente múltiples obstáculos simultáneos.
    /// </summary>
    /// <param name="evasionTurn">Repulsión lateral [-1,1]: >0 gira derecha, <0 gira izquierda.</param>
    /// <param name="frontalThreat">Amenaza frontal [0-1].</param>
    /// <param name="bestFreeAngle">Ángulo (grados) del rayo más despejado (anti-parálisis).</param>
    private void ObtenerRayos(out float evasionTurn, out float frontalThreat, out float bestFreeAngle)
    {
        evasionTurn   = 0f;
        frontalThreat = 0f;
        bestFreeAngle = 0f;

        float repulsionSum = 0f;
        float weightSum    = 0f;
        float bestFreeDist = -1f;

        int   totalRays = 2 * raysPerDirection + 1;
        float angleStep = raysPerDirection > 0 ? maxRayDegrees / raysPerDirection : 0f;
        Vector3 origin  = transform.TransformPoint(new Vector3(0f, 0.33f, 0.492f));

        for (int i = 0; i < totalRays; i++)
        {
            float angle;
            if (i == 0)          angle = 0f;
            else if (i % 2 == 1) angle =  ((i + 1) / 2) * angleStep;   // derecha: +10, +20...
            else                 angle = -((i / 2)       * angleStep);   // izquierda: -10, -20...

            Vector3 forwardFlat = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 dir         = Quaternion.Euler(0, angle, 0) * forwardFlat;

            float freeDist = rayLength; // Por defecto: camino libre

            if (Physics.SphereCast(origin, sphereRadius, dir, out RaycastHit hit, rayLength)
                && hit.distance >= 0.3f
                && hit.collider.CompareTag("Obstacle")
                && hit.distance / rayLength <= dangerFraction)
            {
                freeDist = hit.distance;
                float threat = 1.0f - (hit.distance / rayLength);

                // Amenaza frontal: tercio central del abanico
                if (Mathf.Abs(angle) <= maxRayDegrees / 3f)
                    frontalThreat = Mathf.Max(frontalThreat, threat);

                // Repulsión con suma ponderada:
                //   roca a la DERECHA (angle > 0) → empuja a la izquierda → contribución negativa
                //   roca a la IZQUIERDA (angle < 0) → empuja a la derecha → contribución positiva
                //   roca al CENTRO (angle == 0)    → sin componente lateral
                if (angle != 0f)
                {
                    repulsionSum += -Mathf.Sign(angle) * threat;
                    weightSum    += threat;
                }
            }

            // Rastrear el rayo más despejado (hueco libre más grande)
            if (freeDist > bestFreeDist)
            {
                bestFreeDist  = freeDist;
                bestFreeAngle = angle;
            }
        }

        // Normalizar: resultado en [-1, 1]
        evasionTurn = weightSum > 0f ? Mathf.Clamp(repulsionSum / weightSum, -1f, 1f) : 0f;
    }

    // -----------------------------------------------------------------------
    // ESTADOS TERMINALES (idénticos a HuskyAgent2)
    // -----------------------------------------------------------------------
    private void ComprobarEstadosTerminales()
    {
        float distanceToTarget = Vector3.Distance(transform.position, target.position);

        // Éxito
        if (distanceToTarget <= successDistance)
        {
            totalSuccesses++;
            RegistrarEpisodio("SUCCESS", distanceToTarget);
            DispararFlash(Color.green);
            IniciarEpisodio();
            return;
        }

        // Vuelco o caída
        float relativeY = transform.position.y - terrainGenerator.transform.position.y;
        if (transform.up.y < 0.2f || relativeY < -5f)
        {
            totalFailures++;
            RegistrarEpisodio("FALL", distanceToTarget);
            DispararFlash(Color.blue);
            IniciarEpisodio();
            return;
        }

        // Anti-atasco
        checkTimer++;
        if (checkTimer >= stuckCheckInterval)
        {
            float netDistanceMoved = Vector3.Distance(transform.localPosition, lastPosition);
            float netAngleTurned   = Quaternion.Angle(transform.localRotation, lastRotation);

            if (netDistanceMoved >= 0.5f)
            {
                stuckCounter = 0;
                spinCounter  = 0;
            }
            else
            {
                if (netAngleTurned >= 45.0f)
                {
                    spinCounter++;
                    stuckCounter = 0;
                    if (spinCounter >= maxSpinPermitido)
                    {
                        totalFailures++;
                        RegistrarEpisodio("SPIN", distanceToTarget);
                        DispararFlash(Color.yellow);
                        IniciarEpisodio();
                        return;
                    }
                }
                else
                {
                    stuckCounter++;
                    spinCounter = 0;
                    if (stuckCounter >= maxStuckPermitido)
                    {
                        totalFailures++;
                        RegistrarEpisodio("STUCK", distanceToTarget);
                        DispararFlash(Color.red);
                        IniciarEpisodio();
                        return;
                    }
                }
            }

            lastPosition = transform.localPosition;
            lastRotation = transform.localRotation;
            checkTimer   = 0;
        }
    }

    // -----------------------------------------------------------------------
    // COLISIÓN CON OBSTÁCULO (idéntico a HuskyAgent2)
    // -----------------------------------------------------------------------
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Obstacle"))
        {
            totalFailures++;
            float dist = Vector3.Distance(transform.position, target.position);
            RegistrarEpisodio("COLLISION", dist);
            DispararFlash(Color.magenta);
            IniciarEpisodio();
        }
    }

    // Captura colisiones lentas laterales que OnCollisionEnter puede perder
    private void OnCollisionStay(Collision collision)
    {
        if (collision.gameObject.CompareTag("Obstacle"))
        {
            totalFailures++;
            float dist = Vector3.Distance(transform.position, target.position);
            RegistrarEpisodio("COLLISION_STAY", dist);
            DispararFlash(Color.magenta);
            IniciarEpisodio();
        }
    }

    // -----------------------------------------------------------------------
    // RESET DE EPISODIO (mismo spawn que HuskyAgent2)
    // -----------------------------------------------------------------------
    private void IniciarEpisodio()
    {
        episodeCount++;
        stepCount                = 0;
        totalEnergyThisEpisode   = 0f;
        episodeStartTime         = Time.time;

        int currentSeed;
        if (useFixedSeed)
        {
            currentSeed = envSeed;                                // Modo depuración: semilla única
        }
        else if (useSeedList && seedList != null && seedList.Length > 0)
        {
            if (seedIndex >= seedList.Length)
            {
                // Test completado: cerrar CSV y parar el Play Mode
                Debug.Log($"[HuskyHeuristic] ✅ Test OE8 completado: {seedList.Length} episodios evaluados. CSV guardado en: {System.IO.Path.Combine(Application.persistentDataPath, csvFileName)}");
                if (csvWriter != null) { csvWriter.Flush(); csvWriter.Close(); csvWriter = null; }
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
                return;
            }
            currentSeed = seedList[seedIndex];  // Modo evaluación: lista ordenada
            seedIndex++;
        }
        else
        {
            currentSeed = Random.Range(0, 999999);                // Modo libre: aleatorio
        }

        if (terrainGenerator != null) terrainGenerator.GenerateTerrain(currentSeed);

        // Spawn del rover en el centro del terreno
        Vector3 resetPos = transform.position;
        if (terrainGenerator != null)
        {
            Terrain t            = terrainGenerator.GetComponent<Terrain>();
            Vector3 terrainOrigin = t.transform.position;
            float   centerX      = terrainOrigin.x + (terrainWidthX  / 2f);
            float   centerZ      = terrainOrigin.z + (terrainLengthZ / 2f);
            resetPos             = new Vector3(centerX, 0, centerZ);
            float groundY        = t.SampleHeight(resetPos) + terrainOrigin.y;
            resetPos.y           = groundY + 0.2f;
        }

        float randomYaw = Random.Range(0f, 360f);
        baseLink.TeleportRoot(resetPos, Quaternion.Euler(0, randomYaw, 0));
        baseLink.linearVelocity  = Vector3.zero;
        baseLink.angularVelocity = Vector3.zero;

        // Spawn de la meta (mismo algoritmo que HuskyAgent2: a prueba de balas)
        Vector3 newTargetPos  = Vector3.zero;
        bool    posicionValida = false;
        int     intentos       = 0;

        while (!posicionValida && intentos < 50)
        {
            float   randomAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float   randomDist  = Random.Range(5f, terrainWidthX * 0.4f);
            Vector3 offset      = new Vector3(Mathf.Cos(randomAngle) * randomDist, 0,
                                              Mathf.Sin(randomAngle) * randomDist);
            newTargetPos = resetPos + offset;

            if (terrainGenerator != null)
            {
                Terrain t    = terrainGenerator.GetComponent<Terrain>();
                newTargetPos.y = t.SampleHeight(newTargetPos) + t.transform.position.y + 0.5f;
            }

            Physics.SyncTransforms();
            Collider[] colliders   = Physics.OverlapSphere(newTargetPos, 3.0f);
            bool       chocaConRoca = false;
            foreach (var col in colliders)
            {
                if (col.CompareTag("Obstacle")) { chocaConRoca = true; break; }
            }
            if (!chocaConRoca) posicionValida = true;
            intentos++;
        }

        if (!posicionValida)
            Debug.LogWarning("[HuskyHeuristic] No se encontró sitio libre para la meta tras 50 intentos.");

        target.position            = newTargetPos;
        previousDistanceToTarget   = Vector3.Distance(transform.position, target.position);

        // Reset anti-atasco
        checkTimer       = 0;
        stuckCounter     = 0;
        spinCounter      = 0;
        lastPosition     = transform.localPosition;
        lastRotation     = transform.localRotation;
        inPlaceRotating  = false;
    }

    // -----------------------------------------------------------------------
    // CONTROL DE RUEDAS (idéntico a HuskyAgent2)
    // -----------------------------------------------------------------------
    private void ConfigurarMotores(ArticulationBody[] wheels)
    {
        foreach (var wheel in wheels)
        {
            var drive        = wheel.xDrive;
            drive.stiffness  = 0f;
            drive.damping    = 10f;
            //drive.forceLimit = 1000f;
            drive.forceLimit = 2000f;
            wheel.xDrive     = drive;
        }
    }

    private void AplicarVelocidadAngular(ArticulationBody[] wheels, float linearVel)
    {
        float targetAngularVel = (linearVel / wheelRadius) * Mathf.Rad2Deg;
        foreach (var wheel in wheels)
        {
            var drive             = wheel.xDrive;
            drive.targetVelocity  = targetAngularVel;
            wheel.xDrive          = drive;
        }
    }

    // -----------------------------------------------------------------------
    // FLASH VISUAL (idéntico a HuskyAgent2)
    // -----------------------------------------------------------------------
    private void DispararFlash(Color color)
    {
        if (groundTerrain == null || groundTerrain.materialTemplate == null) return;
        if (flashCoroutine != null) StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(FlashGround(color, 0.5f));
    }

    private IEnumerator FlashGround(Color flashColor, float duration)
    {
        groundTerrain.materialTemplate.color = flashColor;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            groundTerrain.materialTemplate.color =
                Color.Lerp(flashColor, originalGroundColor, elapsed / duration);
            yield return null;
        }
        groundTerrain.materialTemplate.color = originalGroundColor;
    }

    // -----------------------------------------------------------------------
    // REGISTRO DE MÉTRICAS EN CSV (OE8 / OE11)
    // -----------------------------------------------------------------------
    
    private void InicializarCSV()
    {
        string csvDir = Path.Combine(Application.dataPath, "..", "Results", "EvaluationResults", "Heuristic");
        Directory.CreateDirectory(csvDir);

        string path = Path.Combine(csvDir, csvFileName);
        csvWriter = new StreamWriter(path, append: false);
        
        csvWriter.WriteLine("episodio;resultado;pasos;tiempo_s;distancia_final_m;" +
                            "energia_total;tasa_exito_acum");
        Debug.Log($"[HuskyHeuristic] CSV de métricas guardado en: {path}");
    }

    

    private void RegistrarEpisodio(string resultado, float distanciaFinal)
    {
        if (!guardarMetricas || csvWriter == null) return;

        var   ci             = System.Globalization.CultureInfo.InvariantCulture;
        float tiempoEpisodio = Time.time - episodeStartTime;
        float tasaExito      = episodeCount > 0 ? (float)totalSuccesses / episodeCount : 0f;

        csvWriter.WriteLine($"{episodeCount};{resultado};{stepCount};" +
                            $"{tiempoEpisodio.ToString("F2", ci)};{distanciaFinal.ToString("F2", ci)};" +
                            $"{totalEnergyThisEpisode.ToString("F2", ci)};{tasaExito.ToString("F3", ci)}");
        csvWriter.Flush();
    }

    // -----------------------------------------------------------------------
    // VISUALIZACIÓN GIZMOS (Editor)
    // -----------------------------------------------------------------------
    private void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        int   totalRays = 2 * raysPerDirection + 1;
        float angleStep = raysPerDirection > 0 ? maxRayDegrees / raysPerDirection : 0f;
        Vector3 origin  = transform.TransformPoint(new Vector3(0f, 0.33f, 0.492f));

        for (int i = 0; i < totalRays; i++)
        {
            float angle;
            if (i == 0)          angle = 0f;
            else if (i % 2 == 1) angle =  ((i + 1) / 2) * angleStep;
            else                 angle = -((i / 2)       * angleStep);

            Vector3 forwardFlat = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 dir         = Quaternion.Euler(0, angle, 0) * forwardFlat;

            bool hit        = Physics.SphereCast(origin, sphereRadius, dir, out RaycastHit hitInfo, rayLength);
            bool esObstaculo = hit && hitInfo.distance >= 0.3f && hitInfo.collider.CompareTag("Obstacle");
            bool esPeligro   = esObstaculo && (hitInfo.distance / rayLength) <= dangerFraction;

            Gizmos.color = esPeligro   ? Color.red    // Peligro inminente
                         : esObstaculo ? Color.yellow  // Obstáculo detectado pero lejos
                         :               Color.cyan;   // Libre

            Gizmos.DrawRay(origin, dir * rayLength);
        }
    }
}
