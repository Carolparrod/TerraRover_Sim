using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

public class HuskyAgent2 : Agent
{
    [Header("Referencias (OE2) adicional")]
    //public TerrainGenerator3 terrainGenerator; // <-- NUEVA REFERENCIA AL GENERADOR
    public TerrainGenerator4 terrainGenerator;

    [Header("Referencias (OE3)")]
    public ArticulationBody baseLink; // El ArticulationBody raíz del Husky
    public Transform target;          // El destino al que debe llegar

    [Header("Parámetros de Normalización (OE3)")]
    public float maxLinearSpeed = 3.0f;  // m/s
    public float maxAngularSpeed = 3.0f; // rad/s
    private float maxDistanceToTarget; // metros máximos esperados

    [Header("Condiciones de Episodio (OE4)")]
    public float successDistance = 2.0f; // A qué distancia se considera éxito
    public int envSeed = 42; // Semilla controlada para reproducibilidad
    public bool useFixedSeed = false; // Alternar entre entrenamiento (false) y test (true)

    // Variables internas para el reset
    private Vector3 startPosition;
    private Quaternion startRotation;

    [Header("Parámetros del Entorno")]
    public float terrainWidthX = 20f;  // Ancho del terreno
    public float terrainLengthZ = 20f; // Largo del terreno

    [Header("Condiciones de Atasco (OE4/OE5)")]
    public float minVelocityThreshold = 0.1f; // Velocidad mínima en m/s para considerar que se mueve
    public int maxStuckSteps = 150;           // Pasos físicos seguidos (aprox 3 segundos a 50Hz)

    private int stuckCounter = 0;             // Contador interno

    [Header("Pesos de Recompensa (OE5)")]
    public float wAvance = 1.0f;
    public float wEstabilidad = 0.05f;
    public float wEnergia = 0.001f;

    // Variable interna para calcular el avance
    private float previousDistanceToTarget;

    //CAMBIO onActionRecieved + HuskyControllerPrueba4
    [Header("Configuración de Ruedas (Cinemática Diferencial)")]
    public ArticulationBody[] leftWheels;
    public ArticulationBody[] rightWheels;
    public float trackWidth = 0.55f;
    public float wheelRadius = 0.165f;

    public override void Initialize()
    {
        // ------------------------------------------------------------------
        // CONTROL DE FALLOS (Programación Defensiva)
        // ------------------------------------------------------------------
        if (baseLink == null)
        {
            Debug.LogError("[HuskyAgent] ERROR: No has asignado el 'baseLink' (ArticulationBody) en el Inspector.");
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false; // Detiene el Play mode
            #endif
            return;
        }

        if (target == null)
        {
            Debug.LogError("[HuskyAgent] ERROR: No has asignado el 'target' en el Inspector.");
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            #endif
            return;
        }

        // Guardamos la posición inicial del robot al arrancar la simulación
        startPosition = transform.position;
        startRotation = transform.rotation;

        // CÁLCULO AUTOMÁTICO DE LA DISTANCIA MÁXIMA (Diagonal del terreno)
        // Se ejecuta una sola vez al inicio (Coste computacional O(1))
        maxDistanceToTarget = Mathf.Sqrt((terrainWidthX * terrainWidthX) + (terrainLengthZ * terrainLengthZ));

        Debug.Log($"[HuskyAgent] Distancia máxima calculada para normalización: {maxDistanceToTarget} metros.");

        //CAMBIO onActionRecieved: Configuramos los motores de las ruedas (similar a HuskyManualController4)
        ConfigurarMotores(leftWheels);
        ConfigurarMotores(rightWheels);
    }

    //CAMBIO onActionRecieved: Método para configurar los motores de las ruedas, reutilizado del controlador manual
    private void ConfigurarMotores(ArticulationBody[] wheels)
    {
        foreach (var wheel in wheels)
        {
            var drive = wheel.xDrive;
            drive.stiffness = 0f;
            drive.damping = 100f;
            drive.forceLimit = 1000f;
            wheel.xDrive = drive;
        }
    }

    public override void OnEpisodeBegin()
    {
        int currentSeed = useFixedSeed ? envSeed : Random.Range(0, 999999);

        // 1. Generamos el terreno
        if (terrainGenerator != null)
        {
            terrainGenerator.GenerateTerrain(currentSeed);
        }

        stuckCounter = 0;

        // 2. Calculamos la posición X y Z
        Vector3 resetPos = (terrainGenerator != null && terrainGenerator.startPoint != null)
                           ? terrainGenerator.startPoint.position
                           : startPosition;

        // CORRECCIÓN CAÍDAS: Leemos la altura exacta de la nueva montaña en esa posición X/Z
        if (terrainGenerator != null)
        {
            Terrain t = terrainGenerator.GetComponent<Terrain>();
            if (t != null)
            {
                // Buscamos la altura del suelo y le sumamos 1 metro para que el Husky caiga limpio
                float groundY = t.SampleHeight(resetPos) + t.transform.position.y;
                resetPos.y = groundY + 1.0f;
            }
        }

        // 3. Teletransportamos al robot de forma segura
        baseLink.TeleportRoot(resetPos, startRotation);
        baseLink.linearVelocity = Vector3.zero;
        baseLink.angularVelocity = Vector3.zero;

        
        // 4. Reposicionamos el objetivo (ajustando la z al nuevo terreno)
        Vector3 newTargetPos;
        if (terrainGenerator != null && terrainGenerator.goalPoint != null)
        {
            newTargetPos = terrainGenerator.goalPoint.position;
        }
        else
        {
            float randomX = Random.Range(-10f, 10f);
            float randomZ = Random.Range(10f, 30f);
            newTargetPos = startPosition + new Vector3(randomX, 0, randomZ);
        }

        // Magia topográfica: Leemos la altura exacta de la montaña en esa X y Z
        if (terrainGenerator != null)
        {
            Terrain t = terrainGenerator.GetComponent<Terrain>();
            if (t != null)
            {
                // SampleHeight devuelve la altura del terreno. Le sumamos la posición base del Terrain
                float groundY = t.SampleHeight(newTargetPos) + t.transform.position.y;

                // Le sumamos 0.5 metros extra para que la meta "flote" un poco y no se entierre
                newTargetPos.y = groundY + 0.5f;
            }
        }

        // Asignamos la posición final perfecta
        target.position = newTargetPos;

        // 5. Inicializamos la métrica
        previousDistanceToTarget = Vector3.Distance(transform.position, target.position);
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // ------------------------------------------------------------------
        // IMU VIRTUAL Y VECTOR NORMALIZADO
        // ------------------------------------------------------------------

        // Orientación (Ya acotada entre [-1, 1])
        sensor.AddObservation(transform.up);
        sensor.AddObservation(transform.forward);

        // Velocidad Lineal (Actualizado a linearVelocity y normalizado)
        Vector3 localVelocity = transform.InverseTransformDirection(baseLink.linearVelocity);
        sensor.AddObservation(Mathf.Clamp(localVelocity.x / maxLinearSpeed, -1f, 1f));
        sensor.AddObservation(Mathf.Clamp(localVelocity.y / maxLinearSpeed, -1f, 1f));
        sensor.AddObservation(Mathf.Clamp(localVelocity.z / maxLinearSpeed, -1f, 1f));

        // Velocidad Angular (Normalizada)
        Vector3 localAngularVelocity = transform.InverseTransformDirection(baseLink.angularVelocity);
        sensor.AddObservation(Mathf.Clamp(localAngularVelocity.x / maxAngularSpeed, -1f, 1f));
        sensor.AddObservation(Mathf.Clamp(localAngularVelocity.y / maxAngularSpeed, -1f, 1f));
        sensor.AddObservation(Mathf.Clamp(localAngularVelocity.z / maxAngularSpeed, -1f, 1f));

        // Consciencia del Objetivo (Dirección y distancia normalizada)
        Vector3 vectorToTarget = target.position - transform.position;
        Vector3 localDirToTarget = transform.InverseTransformDirection(vectorToTarget.normalized);
        sensor.AddObservation(localDirToTarget);

        float distanceToTarget = vectorToTarget.magnitude;
        sensor.AddObservation(Mathf.Clamp(distanceToTarget / maxDistanceToTarget, 0f, 1f));
    }

    private void FixedUpdate()
    {

        // 1. Damos los puntos continuos
        CalculateDenseRewards();

        // 2. Comprobamos si ha muerto o ganado (Sparse rewards)
        CheckTerminalStates();
    }

    private void CheckTerminalStates()
    {
        // ------------------------------------------------------------------
        // CONDICIÓN DE ÉXITO: Llegar al objetivo
        // ------------------------------------------------------------------
        float distanceToTarget = Vector3.Distance(transform.position, target.position);
        if (distanceToTarget <= successDistance)
        {
            AddReward(1.0f);
            Debug.Log($"[ÉXITO] Episodio terminado. Puntuación final: {GetCumulativeReward()}");
            EndEpisode();
        }

        // ------------------------------------------------------------------
        // CONDICIÓN DE FALLO 1: Vuelco del rover
        // ------------------------------------------------------------------
        if (transform.up.y < 0.2f)
        {
            AddReward(-1.0f);
            Debug.Log($"[VUELCO] Episodio terminado. Puntuación final: {GetCumulativeReward()}");
            EndEpisode();
        }

        // ------------------------------------------------------------------
        // CONDICIÓN DE FALLO 2: Caída del mapa (Fallout)
        // ------------------------------------------------------------------
        if (transform.position.y < -5f)
        {
            AddReward(-1.0f);
            Debug.Log($"[CAÍDA] Episodio terminado. Puntuación final: {GetCumulativeReward()}");
            EndEpisode();
        }

        // ------------------------------------------------------------------
        // CONDICIÓN DE FALLO 3: Atasco prolongado
        // ------------------------------------------------------------------
        // Si la velocidad lineal global es casi cero...
        if (baseLink.linearVelocity.magnitude < minVelocityThreshold)
        {
            stuckCounter++; // Sumamos un paso al contador de atasco

            if (stuckCounter >= maxStuckSteps)
            {
                // Penalización severa por quedarse atascado
                AddReward(-1.0f);
                Debug.Log($"[ATASCO] Episodio terminado. Puntuación final: {GetCumulativeReward()}");
                EndEpisode();
            }
        }
        else
        {
            // Si consigue moverse a una velocidad decente, reseteamos el contador
            stuckCounter = 0;
        }
    }

    private void CalculateDenseRewards()
    {
        // 1. TÉRMINO DE AVANCE (Proximity Reward)
        float currentDistance = Vector3.Distance(transform.position, target.position);
        float distanceDifference = previousDistanceToTarget - currentDistance;

        // Premiamos si se ha acercado (positivo) o castigamos si se ha alejado (negativo)
        float r_avance = distanceDifference * wAvance;
        AddReward(r_avance);

        // Actualizamos la variable para el siguiente frame
        previousDistanceToTarget = currentDistance;

        // 2. TÉRMINO DE ESTABILIDAD
        // transform.up.y es 1 cuando está plano. Si es 0.8, la penalización es -0.2 * peso
        float tiltPenalty = (1.0f - transform.up.y);
        float r_estabilidad = -tiltPenalty * wEstabilidad;
        AddReward(r_estabilidad);

        // 3. TÉRMINO DE ENERGÍA (Eficiencia y suavidad)
        // Penalizamos la velocidad angular alta (giros bruscos) y damos un pequeño castigo
        //float effort = baseLink.angularVelocity.magnitude; ////////////
        //float r_energia = -(effort + 0.01f) * wEnergia;
        //AddReward(r_energia);

        //CAMBIO onActionRecieved: Penalizamos el "volantazo" directamente desde las acciones de giro (turnAction) para fomentar giros más suaves
        AddReward(-0.01f * wEnergia);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // 1. Leemos las decisiones de la IA
        float moveAction = actions.ContinuousActions[0];
        float turnAction = actions.ContinuousActions[1];

        // 2. Cálculo cinemático diferencial (Tu código manual adaptado)
        float desiredLinear = moveAction * maxLinearSpeed;
        float desiredAngular = turnAction * maxAngularSpeed;

        float leftVelocityMPS = desiredLinear + (desiredAngular * (trackWidth / 2f));
        float rightVelocityMPS = desiredLinear - (desiredAngular * (trackWidth / 2f));

        // Normalización si superamos los límites
        float maxCalculatedVel = Mathf.Max(Mathf.Abs(leftVelocityMPS), Mathf.Abs(rightVelocityMPS));
        if (maxCalculatedVel > maxLinearSpeed + (maxAngularSpeed * trackWidth / 2f))
        {
            float scale = (maxLinearSpeed + (maxAngularSpeed * trackWidth / 2f)) / maxCalculatedVel;
            leftVelocityMPS *= scale;
            rightVelocityMPS *= scale;
        }

        // 3. Aplicar velocidades a los motores (xDrive)
        AplicarVelocidadAngular(leftWheels, leftVelocityMPS);
        AplicarVelocidadAngular(rightWheels, rightVelocityMPS);

        // 4. EL CASTIGO DEL PROFESOR: Penalizar el "volantazo"
        float steeringPenalty = Mathf.Abs(turnAction) * wEnergia;
        AddReward(-steeringPenalty);
    }

    private void AplicarVelocidadAngular(ArticulationBody[] wheels, float linearVelocityMPS)
    {
        float targetAngularVelocityDeg = (linearVelocityMPS / wheelRadius) * Mathf.Rad2Deg;
        foreach (var wheel in wheels)
        {
            var drive = wheel.xDrive;
            drive.targetVelocity = targetAngularVelocityDeg;
            wheel.xDrive = drive;
        }
    }
}