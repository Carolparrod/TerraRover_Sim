using System.Collections;
using System.IO;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

public class HuskyAgent2 : Agent
{
    [Header("Visual Feedback")]
    public Terrain groundTerrain; 
    private Color originalGroundColor;
    private Coroutine flashGroundCoroutine;
    
    [Header("Referencias (OE2)")]
    public TerrainGenerator4 terrainGenerator;

    [Header("Referencias (OE3)")]
    public ArticulationBody baseLink;
    public Transform target;

    [Header("Parámetros de Normalización (OE3)")]
    public float maxLinearSpeed = 1.5f;
    public float maxAngularSpeed = 2f;
    // Nota: Ya no usamos maxDistanceToTarget para evitar colapso en mapas grandes [Problema 2]

    [Header("Condiciones de Episodio (OE4)")]
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
        Debug.Log($"[HuskyAgent2] Lista de {numSeedsToGenerate} semillas generada con masterSeed={masterSeed}.");
    }

    [Header("Detección de Atasco (OE4 - Anti-Hacking)")]
    public float stuckRadiusThreshold = 0.1f; // 10 cm mínimos
    public float minSpinAngle = 2.0f;         // 2 grados mínimos para considerarlo giro
    public int stuckCheckInterval = 50;       // Comprobamos cada 50 pasos (1 segundo)
    public int maxStuckPermitido = 3;         // Falla si está 3 segundos atascado
    public int maxSpinPermitido = 10;          // Falla si está 4 segundos girando sin acercarse

    private int checkTimer = 0;
    private int stuckCounter = 0;
    private int spinCounter = 0;
    private Vector3 lastPosition;
    private Quaternion lastRotation;

    [Header("Límites del Terreno (OE6)")]
    public float terrainWidthX = 50f;
    public float terrainLengthZ = 50f;

    [Header("Pesos de Recompensa (OE5)")]
    public float wAvance = 1.0f;
    public float wEstabilidad = 0.05f;
    public float wEnergia = 0.0001f; // Reducido para evitar parálisis
    public float wAlineacion = 0.01f; // NUEVO (Fase 2): Castigo por desalineación

    private Vector3 startPosition;
    private Quaternion startRotation;
    private float previousDistanceToTarget;

    // -----------------------------------------------------------------------
    // MÉTRICAS (OE8)
    // -----------------------------------------------------------------------
    [Header("Registro de Métricas (OE8)")]
    [Tooltip("Activa el guardado de métricas en CSV para análisis estadístico")]
    public bool   guardarMetricas = true;
    public string csvFileName     = "HuskyAgent2_metricas.csv";

    private StreamWriter csvWriter;
    private int   episodeCount           = 0;
    private int   stepCount              = 0;
    private int   totalSuccesses         = 0;
    private int   totalFailures          = 0;
    private float episodeStartTime       = 0f;
    private float totalEnergyThisEpisode = 0f;

    [Header("Configuración de Ruedas")]
    public ArticulationBody[] leftWheels;
    public ArticulationBody[] rightWheels;
    public float trackWidth = 0.55f;
    public float wheelRadius = 0.165f;

    [Header("Penalización de Proximidad LiDAR (OE6)")]
    [Tooltip("Radio de peligro: distancia normalizada [0-1] a partir de la cual empieza el castigo")]
    public float proximityDangerThreshold = 0.3f; // 30% del rango del rayo
    [Tooltip("Peso máximo de la penalización cuando el obstáculo está a distancia 0")]
    public float wProximidad = 0.5f;


    public override void Initialize()
    {
        if (baseLink == null || target == null)
        {
            Debug.LogError("[HuskyAgent] Faltan referencias en el Inspector.");
            return;
        }

        startPosition = transform.position;
        startRotation = transform.rotation;

        ConfigurarMotores(leftWheels);
        ConfigurarMotores(rightWheels);

        if (groundTerrain != null && groundTerrain.materialTemplate != null)
        {
            // Creamos un clon del material SOLO para este rover.
            // Así los destellos no afectan a los otros mapas paralelos.
            groundTerrain.materialTemplate = new Material(groundTerrain.materialTemplate);
            originalGroundColor = groundTerrain.materialTemplate.color;
        }

        if (guardarMetricas)
            InicializarCSV();
    }

    private void OnDestroy()
    {
        if (csvWriter != null)
        {
            csvWriter.Flush();
            csvWriter.Close();
        }
    }

    private void ConfigurarMotores(ArticulationBody[] wheels)
    {
        foreach (var wheel in wheels)
        {
            var drive = wheel.xDrive;
            drive.stiffness = 0f;
            drive.damping = 10f;
            drive.forceLimit = 1000f;
            wheel.xDrive = drive;
        }
    }

    public override void OnEpisodeBegin()
    {


        episodeCount++;
        stepCount              = 0;
        totalEnergyThisEpisode = 0f;
        episodeStartTime       = Time.time;

        int currentSeed;
        if (useFixedSeed)
        {
            currentSeed = envSeed;                          // Modo depuración: semilla única
        }
        else if (useSeedList && seedList != null && seedList.Length > 0)
        {
            if (seedIndex >= seedList.Length)
            {
                // Test completado: cerrar CSV y parar el Play Mode
                Debug.Log($"[HuskyAgent2] ✅ Test OE8 completado: {seedList.Length} episodios evaluados. CSV guardado en: {System.IO.Path.Combine(Application.persistentDataPath, csvFileName)}");
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
            currentSeed = Random.Range(0, 999999);          // Modo entrenamiento: aleatorio
        }

        // 1. Generar Terreno
        if (terrainGenerator != null) terrainGenerator.GenerateTerrain(currentSeed);

        // 3. Posicionar Rover (Spawn Central para evitar sesgo direccional)
        Vector3 resetPos = startPosition;

        if (terrainGenerator != null)
        {
            Terrain t = terrainGenerator.GetComponent<Terrain>();
            Vector3 terrainOrigin = t.transform.position;

            // Calculamos el centro exacto de ESTE mapa específico
            float centerX = terrainOrigin.x + (terrainWidthX / 2f);
            float centerZ = terrainOrigin.z + (terrainLengthZ / 2f);

            resetPos = new Vector3(centerX, 0, centerZ);

            // Ajustamos la altura para que no atraviese el suelo
            float groundY = t.SampleHeight(resetPos) + terrainOrigin.y;
            resetPos.y = groundY + 0.2f;
        }

        // Teletransportamos al robot al centro, dándole también una rotación inicial aleatoria
        // para maximizar la generalización en todas las orientaciones posibles.
        float randomYaw = Random.Range(0f, 360f);
        Quaternion randomSpawnRotation = Quaternion.Euler(0, randomYaw, 0);

        baseLink.TeleportRoot(resetPos, randomSpawnRotation);
        baseLink.linearVelocity = Vector3.zero;
        baseLink.angularVelocity = Vector3.zero;


        // 4. Spawn Aleatorio del Objetivo (Problema 3 - Generalización)
        /*float randomAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        float randomDist = Random.Range(5f, terrainWidthX * 0.4f);

        Vector3 offset = new Vector3(Mathf.Cos(randomAngle) * randomDist, 0, Mathf.Sin(randomAngle) * randomDist);
        Vector3 newTargetPos = resetPos + offset;

        
        if (terrainGenerator != null)
        {
            
            Terrain t = terrainGenerator.GetComponent<Terrain>();
            newTargetPos.y = t.SampleHeight(newTargetPos) + t.transform.position.y + 0.5f;
        }

        target.position = newTargetPos;
        previousDistanceToTarget = Vector3.Distance(transform.position, target.position);*/
        // 3. Spawn Aleatorio del Objetivo (A prueba de balas definitivo) 
        Vector3 newTargetPos = Vector3.zero;
        bool posicionValida = false;
        int intentos = 0;

        // Subimos la paciencia a 50 intentos
        while (!posicionValida && intentos < 50)
        {
            float randomAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float randomDist = Random.Range(5f, terrainWidthX * 0.4f);
            Vector3 offset = new Vector3(Mathf.Cos(randomAngle) * randomDist, 0, Mathf.Sin(randomAngle) * randomDist);
            newTargetPos = resetPos + offset;

            if (terrainGenerator != null)
            {
                Terrain t = terrainGenerator.GetComponent<Terrain>();
                newTargetPos.y = t.SampleHeight(newTargetPos) + t.transform.position.y + 0.5f;
            }

            // Obligamos a Unity a registrar las colisiones
            Physics.SyncTransforms();

            // Lanzamos una esfera de TRES METROS (mucho más grande que tu aura)
            Collider[] colliders = Physics.OverlapSphere(newTargetPos, 3.0f);
            bool chocaConRoca = false;

            foreach (var col in colliders)
            {
                if (col.CompareTag("Obstacle"))
                {
                    chocaConRoca = true;
                    break;
                }
            }

            if (!chocaConRoca)
            {
                posicionValida = true; // ¡Sitio libre!
            }
            intentos++;
        }

        // ALARMA: Si después de 50 intentos no encuentra sitio, nos avisa en la consola
        if (!posicionValida)
        {
            Debug.LogWarning("[HuskyAgent] ¡Aviso! No se encontró un sitio libre para la meta tras 50 intentos. Revisa la densidad de rocas.");
        }

        target.position = newTargetPos;
        previousDistanceToTarget = Vector3.Distance(transform.position, target.position);

        // NUEVO: Reset de la memoria anti-atascos de la lógica del profesor
        checkTimer = 0;
        stuckCounter = 0;
        spinCounter = 0;
        lastPosition = transform.localPosition;
        lastRotation = transform.localRotation;

        


    }
    
    private IEnumerator FlashGround(Color flashColor, float duration)
    {
        if (groundTerrain == null || groundTerrain.materialTemplate == null) yield break;

        // Ponemos el color de golpe (rojo o verde)
        groundTerrain.materialTemplate.color = flashColor;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            // Lo vamos difuminando de vuelta a su color original
            groundTerrain.materialTemplate.color = Color.Lerp(flashColor, originalGroundColor, elapsed / duration);
            yield return null;
        }

        // Nos aseguramos de que termine en su color exacto
        groundTerrain.materialTemplate.color = originalGroundColor;
    }

    // Función para disparar el color fácilmente
    private void DispararFlash(Color colorFlash)
    {
        if (groundTerrain != null && groundTerrain.materialTemplate != null)
        {
            if (flashGroundCoroutine != null)
            {
                StopCoroutine(flashGroundCoroutine);
            }
            flashGroundCoroutine = StartCoroutine(FlashGround(colorFlash, 0.5f));
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // IMU: Orientación local (Invariante a posición global) [cite: 8]
        sensor.AddObservation(transform.up);
        sensor.AddObservation(transform.forward);

        // Velocidades normalizadas
        Vector3 localVelocity = transform.InverseTransformDirection(baseLink.linearVelocity);
        sensor.AddObservation(Vector3.ClampMagnitude(localVelocity / maxLinearSpeed, 1f));

        Vector3 localAngularVelocity = transform.InverseTransformDirection(baseLink.angularVelocity);
        sensor.AddObservation(Vector3.ClampMagnitude(localAngularVelocity / maxAngularSpeed, 1f));

        //Vector dirección normalizado (Problema 2 - Out of Distribution) 
        Vector3 vectorToTarget = target.position - transform.position;
        Vector3 localDirToTarget = transform.InverseTransformDirection(vectorToTarget.normalized);
        sensor.AddObservation(localDirToTarget);

        // Distancia relativa (opcional, pero normalizada siempre entre 0 y 1 para mapas de cualquier tamaño)
        sensor.AddObservation(Mathf.Clamp01(vectorToTarget.magnitude / 100f));
    }

    private void FixedUpdate()
    {
        stepCount++;
        CalculateDenseRewards();
        CheckTerminalStates();
    }

    private void CalculateDenseRewards()
    {
        // 1. Avance
        float currentDistance = Vector3.Distance(transform.position, target.position);
        float distanceDifference = previousDistanceToTarget - currentDistance;
        AddReward(distanceDifference * wAvance);
        previousDistanceToTarget = currentDistance;

        // 2. Penalización por Tiempo (Suave para evitar temeridad)
        AddReward(-0.0005f);

        // 3. Estabilidad Cuadrática (Castiga solo inclinaciones fuertes) 
        float tilt = 1.0f - transform.up.y;
        if (tilt > 0.1f)
        {
            AddReward(-(tilt * tilt) * wEstabilidad);
        }

        //4. NUEVO (Fase 2): Penalización estricta por Desalineación (Gradiente continuo)
        float currentSpeed = baseLink.linearVelocity.magnitude;

        if (currentSpeed > 0.1f)
        {
            Vector3 moveDirection = baseLink.linearVelocity.normalized;
            float alignment = Vector3.Dot(transform.forward, moveDirection);

            // Si no está alineado casi perfectamente hacia adelante (margen de ~36 grados)
            if (alignment < 0.5f) //if (alignment < 0.8f)
            {
                // La fórmula (alignment - 1.0f) garantiza que la penalización sea progresiva.
                // Cuanto más se desvíe del 1.0 perfecto, mayor será el castigo (negativo).
                AddReward((alignment - 1.0f) * currentSpeed * wAlineacion);
            }
        }

        // 5. NUEVO: La Brújula del Dolor (Romper la simetría estática/tembleque)
        // Calculamos hacia dónde mira el robot en relación a la meta (independiente de si se mueve o no)
        /*Vector3 dirToTarget = (target.position - transform.position).normalized;                                       /////// I
        /*float lookAlignment = Vector3.Dot(transform.forward, dirToTarget);                                             //////  V

        // Si no está mirando hacia la meta (lookAlignment es menor a 0.5, aprox 60 grados de desviación)
        // Esto le castiga por el simple hecho de darle la espalda al objetivo, forzándolo a girar el morro.
        if (lookAlignment < 0.0f) //if (lookAlignment < 0.5f)                                                           ////////////Esta es la funcion normal, la de abajo es por si se queda cerca el rover del objetivo y hace algo raro
            {
            // El multiplicador 0.002f es bajito para que no se asuste, 
            // pero lo suficiente para que el "tembleque" infinito le salga caro.
            AddReward((lookAlignment - 1.0f) * 0.002f);
        }*/
        // Medimos a qué distancia está de la meta
        /*float distanciaALaMeta = Vector3.Distance(transform.position, target.position);

        // SOLO le cobramos el impuesto de la Brújula si está LEJOS de la meta
        if (distanciaALaMeta > 3.0f)
        {
            Vector3 dirToTarget = (target.position - transform.position).normalized;
            float lookAlignment = Vector3.Dot(transform.forward, dirToTarget);

            if (lookAlignment < 0.0f)
            {
                AddReward((lookAlignment - 1.0f) * 0.002f);
            }
        }*/
        // 6. NUEVO (Fase 3 - OE6): Penalización progresiva por proximidad a obstáculos
        float penalizacionLidar = CalcularPenalizacionProximidad();
        if (penalizacionLidar < 0f)
        {
            AddReward(penalizacionLidar);
        }

    }

    private void CheckTerminalStates()
    {
        float distanceToTarget = Vector3.Distance(transform.position, target.position);

        // Éxito
        if (distanceToTarget <= successDistance)
        {
            AddReward(2.0f);
            totalSuccesses++;
            RegistrarEpisodio("SUCCESS", distanceToTarget);
            DispararFlash(Color.green);
            EndEpisode();
            return;
        }

        /*// Vuelco o Caída 
        if (transform.up.y < 0.2f || transform.position.y < -5f)
        {
            AddReward(-1.0f);
            Debug.Log($"[FALLO - CAÍDA] El robot cayó al vacío. Altura Y = {transform.position.y}");
            EndEpisode();
            return;
        }*/
        float relativeY = transform.position.y - terrainGenerator.transform.position.y;
        if (transform.up.y < 0.2f || relativeY < -5f)
        {
            AddReward(-1.0f);
            totalFailures++;
            RegistrarEpisodio("FALL", distanceToTarget);
            DispararFlash(Color.blue);
            EndEpisode();
            return;
        }

    
        // --- LÓGICA ANTI-ATASCO (Filtro de Desplazamiento Neto) ---
        checkTimer++;
        if (checkTimer >= stuckCheckInterval)
        {
            float netDistanceMoved = Vector3.Distance(transform.localPosition, lastPosition);
            float netAngleTurned = Quaternion.Angle(transform.localRotation, lastRotation);

            if (netDistanceMoved >= 0.5f)
            {
                // Se ha movido medio metro de verdad. Le perdonamos todo.
                stuckCounter = 0;
                spinCounter = 0;
            }
            else
            {
                if (netAngleTurned >= 45.0f)
                {
                    // No avanza, pero ha girado de verdad (Mínimo 45 grados).
                    spinCounter++;
                    stuckCounter = 0;

                    if (spinCounter >= maxSpinPermitido)
                    {
                        AddReward(-1.0f);
                        totalFailures++;
                        RegistrarEpisodio("SPIN", distanceToTarget);
                        DispararFlash(Color.yellow);
                        EndEpisode();
                        return;
                    }
                }
                else
                {
                    // Tembleque o Congelado: Ni avanza 0.5m, ni gira 45º.
                    stuckCounter++;
                    spinCounter = 0;

                    if (stuckCounter >= maxStuckPermitido)
                    {
                        AddReward(-1.0f);
                        totalFailures++;
                        RegistrarEpisodio("STUCK", distanceToTarget);
                        DispararFlash(Color.red);
                        EndEpisode();
                        return;
                    }
                }
            }

            // Guardamos la foto para compararla dentro de otros 3 segundos (150 steps)
            lastPosition = transform.localPosition;
            lastRotation = transform.localRotation;
            checkTimer = 0;
        }
    }

    // --- NUEVO: LÓGICA DE SEGURIDAD (Prevención de Daños) ---
    private void OnCollisionEnter(Collision collision)
    {
        // Si tocamos cualquier cosa etiquetada como "Obstaculo"...
        if (collision.gameObject.CompareTag("Obstacle"))
        {
            AddReward(-1.0f);
            totalFailures++;
            float dist = Vector3.Distance(transform.position, target.position);
            RegistrarEpisodio("COLLISION", dist);
            DispararFlash(Color.magenta);
            EndEpisode();
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        float moveAction = actions.ContinuousActions[0];
        float turnAction = actions.ContinuousActions[1];

        float desiredLinear = moveAction * maxLinearSpeed;
        float desiredAngular = turnAction * maxAngularSpeed;

        float leftVel = desiredLinear + (desiredAngular * (trackWidth / 2f));
        float rightVel = desiredLinear - (desiredAngular * (trackWidth / 2f));

        AplicarVelocidadAngular(leftWheels, leftVel);
        AplicarVelocidadAngular(rightWheels, rightVel);

        // Acumular energía para métricas OE8
        totalEnergyThisEpisode += Mathf.Abs(leftVel) + Mathf.Abs(rightVel);

        // Penalización por volantazo
        AddReward(-Mathf.Abs(turnAction) * wEnergia);

        
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActionsOut = actionsOut.ContinuousActions;
        continuousActionsOut[0] = Input.GetAxis("Vertical");
        continuousActionsOut[1] = Input.GetAxis("Horizontal");
    }

    private void AplicarVelocidadAngular(ArticulationBody[] wheels, float linearVel)
    {
        float targetAngularVel = (linearVel / wheelRadius) * Mathf.Rad2Deg;
        foreach (var wheel in wheels)
        {
            var drive = wheel.xDrive;
            drive.targetVelocity = targetAngularVel;
            wheel.xDrive = drive;
        }
    }

    // -----------------------------------------------------------------------
    // REGISTRO DE MÉTRICAS EN CSV (OE8)
    // Mismas columnas que HuskyHeuristic para comparación directa.
    // -----------------------------------------------------------------------
    private void InicializarCSV()
    {
        string path = Path.Combine(Application.persistentDataPath, csvFileName);
        csvWriter   = new StreamWriter(path, append: false);
        csvWriter.WriteLine("episodio;resultado;pasos;tiempo_s;distancia_final_m;" +
                            "energia_total;tasa_exito_acum");
        Debug.Log($"[HuskyAgent2] CSV de métricas: {path}");
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

    // --- Lee el LiDAR y devuelve la penalización de proximidad ---
    private float CalcularPenalizacionProximidad()
    {
        // Obtenemos todos los RayPerceptionSensor del agente (puede haber más de uno)
        var raySensors = GetComponentsInChildren<Unity.MLAgents.Sensors.RayPerceptionSensorComponent3D>();

        float maxThreat = 0f; // Guardamos la amenaza MÁS CERCANA de todos los rayos

        foreach (var sensorComp in raySensors)
        {
            // Pedimos el último resultado de percepción del sensor
            var output = sensorComp.RaySensor?.RayPerceptionOutput;
            if (output == null) continue;

            foreach (var rayResult in output.RayOutputs)
            {
                // rayResult.HitFraction: 0.0 = impacto justo en la nariz, 1.0 = no tocó nada
                // Solo nos importan los rayos que DETECTARON un obstáculo
                if (!rayResult.HasHit) continue;

                // Opcional pero recomendado: filtrar por tag para ignorar el suelo
                // Si tu LiDAR ya tiene DetectableTags configurado solo para "Obstacle", no es necesario
                // if (rayResult.HitGameObject != null && !rayResult.HitGameObject.CompareTag("Obstacle")) continue;

                // Calculamos la "amenaza": cuanto más cerca (HitFraction → 0), mayor la amenaza
                float threat = 1.0f - rayResult.HitFraction;

                // Solo nos importa si supera el umbral de peligro
                // proximityDangerThreshold define desde qué distancia relativa empieza el castigo
                float normalizedThreat = Mathf.InverseLerp(1.0f - proximityDangerThreshold, 1.0f, threat);

                if (normalizedThreat > maxThreat)
                    maxThreat = normalizedThreat;
            }
        }

        // La penalización es cuadrática para que sea suave de lejos y fuerte de cerca
        // maxThreat=0 → penalización=0 | maxThreat=1 → penalización=wProximidad
        return -(maxThreat * maxThreat) * wProximidad;
    }
}