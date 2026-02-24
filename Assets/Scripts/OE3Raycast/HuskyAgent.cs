using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;

public class HuskyAgent : Agent
{
    [Header("Referencias")]
    public ArticulationBody baseLink; // El ArticulationBody ra�z del Husky
    public Transform target;          // El destino al que debe llegar

    [Header("Par�metros de Normalizaci�n")]
    // Velocidades m�ximas estimadas para normalizar entre -1 y 1
    public float maxLinearSpeed = 3.0f;  // m/s
    public float maxAngularSpeed = 3.0f; // rad/s
    public float maxDistanceToTarget = 50.0f; // metros m�ximos esperados

    public override void CollectObservations(VectorSensor sensor)
    {
        // ------------------------------------------------------------------
        // 1. IMU VIRTUAL: ORIENTACI�N (6 observaciones)
        // Usamos vectores directores locales respecto al mundo.
        // Ya est�n impl�citamente normalizados entre [-1, 1].
        // ------------------------------------------------------------------
        sensor.AddObservation(transform.up);      // (3) Detecta el vuelco (Pitch/Roll)
        sensor.AddObservation(transform.forward); // (3) Detecta el rumbo actual (Yaw)

        // ------------------------------------------------------------------
        // 2. IMU VIRTUAL: VELOCIDAD LINEAL (3 observaciones)
        // Transformamos la velocidad global a local (del robot) y normalizamos.
        // ------------------------------------------------------------------
        Vector3 localVelocity = transform.InverseTransformDirection(baseLink.linearVelocity);
        sensor.AddObservation(Mathf.Clamp(localVelocity.x / maxLinearSpeed, -1f, 1f)); // Derrape lateral
        sensor.AddObservation(Mathf.Clamp(localVelocity.y / maxLinearSpeed, -1f, 1f)); // Subida/Bajada vertical
        sensor.AddObservation(Mathf.Clamp(localVelocity.z / maxLinearSpeed, -1f, 1f)); // Avance/Retroceso

        // ------------------------------------------------------------------
        // 3. IMU VIRTUAL: VELOCIDAD ANGULAR (3 observaciones)
        // Transformamos de global a local y normalizamos.
        // ------------------------------------------------------------------
        Vector3 localAngularVelocity = transform.InverseTransformDirection(baseLink.angularVelocity);
        sensor.AddObservation(Mathf.Clamp(localAngularVelocity.x / maxAngularSpeed, -1f, 1f));
        sensor.AddObservation(Mathf.Clamp(localAngularVelocity.y / maxAngularSpeed, -1f, 1f));
        sensor.AddObservation(Mathf.Clamp(localAngularVelocity.z / maxAngularSpeed, -1f, 1f));

        // ------------------------------------------------------------------
        // 4. CONSCIENCIA DEL OBJETIVO (4 observaciones)
        // Direcci�n y distancia al destino para que sepa hacia d�nde ir.
        // ------------------------------------------------------------------
        Vector3 vectorToTarget = target.position - transform.position;

        // La direcci�n debe ser local al robot para que tenga sentido espacial
        Vector3 localDirToTarget = transform.InverseTransformDirection(vectorToTarget.normalized);
        sensor.AddObservation(localDirToTarget); // (3) Direcci�n normalizada [-1, 1]

        // Normalizamos la distancia entre [0, 1]
        float distanceToTarget = vectorToTarget.magnitude;
        sensor.AddObservation(Mathf.Clamp(distanceToTarget / maxDistanceToTarget, 0f, 1f)); // (1)
    }
}