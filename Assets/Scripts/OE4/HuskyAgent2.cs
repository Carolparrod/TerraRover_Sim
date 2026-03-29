using System.Collections;
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
    public int envSeed = 42;
    public bool useFixedSeed = false;

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

        if (groundTerrain != null && groundTerrain.materialTemplate != null)
        {
            // Creamos un clon del material SOLO para este rover. 
            // Así los destellos no afectan a los otros mapas paralelos.
            groundTerrain.materialTemplate = new Material(groundTerrain.materialTemplate);
            originalGroundColor = groundTerrain.materialTemplate.color;
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


        int currentSeed = useFixedSeed ? envSeed : Random.Range(0, 999999);

        // 1. Generar Terreno [cite: 7]
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

        // 4. NUEVO (Fase 2): Penalización estricta por Desalineación (Gradiente continuo)
        float currentSpeed = baseLink.linearVelocity.magnitude;

        if (currentSpeed > 0.1f)
        {
            Vector3 moveDirection = baseLink.linearVelocity.normalized;
            float alignment = Vector3.Dot(transform.forward, moveDirection);

            // Si no está alineado casi perfectamente hacia adelante (margen de ~36 grados)
            if (alignment < 0.8f)
            {
                // La fórmula (alignment - 1.0f) garantiza que la penalización sea progresiva.
                // Cuanto más se desvíe del 1.0 perfecto, mayor será el castigo (negativo).
                AddReward((alignment - 1.0f) * currentSpeed * wAlineacion);
            }
        }

        // 5. NUEVO: La Brújula del Dolor (Romper la simetría estática/tembleque)
        // Calculamos hacia dónde mira el robot en relación a la meta (independiente de si se mueve o no)
        Vector3 dirToTarget = (target.position - transform.position).normalized;
        float lookAlignment = Vector3.Dot(transform.forward, dirToTarget);

        // Si no está mirando hacia la meta (lookAlignment es menor a 0.5, aprox 60 grados de desviación)
        // Esto le castiga por el simple hecho de darle la espalda al objetivo, forzándolo a girar el morro.
        if (lookAlignment < 0.5f)
        {
            // El multiplicador 0.002f es bajito para que no se asuste, 
            // pero lo suficiente para que el "tembleque" infinito le salga caro.
            AddReward((lookAlignment - 1.0f) * 0.002f);
        }

    }

    private void CheckTerminalStates()
    {
        float distanceToTarget = Vector3.Distance(transform.position, target.position);

        // Éxito
        if (distanceToTarget <= successDistance)
        {
            AddReward(2.0f);
            //Debug.Log($"[ÉXITO] Meta alcanzada. Recompensa otorgada.");
            DispararFlash(Color.green); // <-- LUZ VERDE
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
            //Debug.Log($"[FALLO - CAÍDA] El robot cayó al vacío.");
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
            AddReward(-1.0f); // Multa máxima por romper el robot
            // Debug.Log("[FALLO] Choque con obstáculo.");
            DispararFlash(Color.magenta); 
            EndEpisode(); // Muerte instantánea
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