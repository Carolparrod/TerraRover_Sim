using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

public class HuskyAgent2 : Agent
{
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
    public int envSeed = 42;
    public bool useFixedSeed = false;

    [Header("Detección de Atasco (OE4 - Anti-Hacking)")]
    public float stuckRadiusThreshold = 0.5f; // Radio mínimo a recorrer [Problema 1]
    public int stuckCheckInterval = 200;      // Pasos físicos (aprox 4s a 50Hz)
    private Vector3 lastStuckCheckPosition;
    private int stuckCheckCounter = 0;

    [Header("Límites del Terreno (OE6)")]
    public float terrainWidthX = 50f;
    public float terrainLengthZ = 50f;

    [Header("Pesos de Recompensa (OE5)")]
    public float wAvance = 1.0f;
    public float wEstabilidad = 0.05f;
    public float wEnergia = 0.0001f; // Reducido para evitar parálisis

    private Vector3 startPosition;
    private Quaternion startRotation;
    private float previousDistanceToTarget;

    [Header("Configuración de Ruedas")]
    public ArticulationBody[] leftWheels;
    public ArticulationBody[] rightWheels;
    public float trackWidth = 0.55f;
    public float wheelRadius = 0.165f;

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
        int currentSeed = useFixedSeed ? envSeed : Random.Range(0, 999999);

        // 1. Generar Terreno [cite: 7]
        if (terrainGenerator != null) terrainGenerator.GenerateTerrain(currentSeed);

        // 2. Reset de lógica de atasco
        stuckCheckCounter = 0;
        lastStuckCheckPosition = transform.position;

        
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
        float randomAngle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        float randomDist = Random.Range(5f, terrainWidthX * 0.4f);

        Vector3 offset = new Vector3(Mathf.Cos(randomAngle) * randomDist, 0, Mathf.Sin(randomAngle) * randomDist);
        Vector3 newTargetPos = resetPos + offset;

        
        if (terrainGenerator != null)
        {
            
            Terrain t = terrainGenerator.GetComponent<Terrain>();
            newTargetPos.y = t.SampleHeight(newTargetPos) + t.transform.position.y + 0.5f;
        }

        target.position = newTargetPos;
        previousDistanceToTarget = Vector3.Distance(transform.position, target.position);

        
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
    }

    private void CheckTerminalStates()
    {
        float distanceToTarget = Vector3.Distance(transform.position, target.position);

        // Éxito
        if (distanceToTarget <= successDistance)
        {
            AddReward(2.0f);
            Debug.Log($"[ÉXITO] Meta alcanzada. Recompensa otorgada.");
            EndEpisode();
            return;
        }

        // Vuelco o Caída 
        if (transform.up.y < 0.2f || transform.position.y < -5f)
        {
            AddReward(-1.0f);
            Debug.Log($"[FALLO - CAÍDA] El robot cayó al vacío. Altura Y = {transform.position.y}");
            EndEpisode();
            return;
        }

        // SOLUCIÓN: Atasco por desplazamiento neto (Problema 1) 
        stuckCheckCounter++;
        if (stuckCheckCounter >= stuckCheckInterval)
        {
            float displacement = Vector3.Distance(transform.position, lastStuckCheckPosition);
            if (displacement < stuckRadiusThreshold)
            {
                AddReward(-1.0f);
                Debug.Log($"[FALLO - ATASCO] Solo avanzó {displacement}m en {stuckCheckInterval} pasos.");
                EndEpisode();
                return;
            }
            else
            {
                lastStuckCheckPosition = transform.position;
                stuckCheckCounter = 0;
            }
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

        // Penalización por volantazo 
        AddReward(-Mathf.Abs(turnAction) * wEnergia);

        // NUEVO, ACTIVAR A PARTIR DE INFANTIL: Penalización suave por ir marcha atrás
        // moveAction va de -1 a 1. Si es menor que 0, es marcha atrás.
        if (moveAction < 0)
        {
            // Como moveAction ya es negativo, al multiplicarlo por un peso positivo, restará recompensa.
            AddReward(moveAction * 0.01f);
        }
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
}