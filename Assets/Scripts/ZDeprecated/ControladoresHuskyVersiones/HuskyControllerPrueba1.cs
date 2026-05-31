using UnityEngine;

public class HuskyControllerPrueba1 : MonoBehaviour
{
    [Header("Configuración de Ruedas")]
    public ArticulationBody[] leftWheels;
    public ArticulationBody[] rightWheels;

    [Header("Parámetros de Movimiento")]
    public float maxLinearSpeed = 1.0f; // m/s
    public float maxAngularSpeed = 2.0f; // rad/s

    void Start()
    {
        // 1. Ajuste del Centro de Masa (Paso 5 del informe)
        // Lo situamos por debajo del eje de las ruedas para máxima estabilidad
        ArticulationBody root = GetComponent<ArticulationBody>();
        if (root != null)
        {
            root.centerOfMass = new Vector3(0, -0.3f, 0);
        }

        // 2. Configuración de los Drives de las ruedas (Paso 4 del informe)
        ConfigurarMotores(leftWheels);
        ConfigurarMotores(rightWheels);
    }

    void ConfigurarMotores(ArticulationBody[] wheels)
    {
        foreach (var wheel in wheels)
        {
            var drive = wheel.xDrive;
            drive.stiffness = 0f;        // Cero absoluto para evitar oposición elástica 
            drive.damping = 1000f;       // Valor alto para vencer inercia
            drive.forceLimit = 100000f;  // Fuerza suficiente para mover el peso real
            wheel.xDrive = drive;
        }
    }

    void FixedUpdate() // Siempre en FixedUpdate para consistencia física 
    {
        float move = Input.GetAxis("Vertical");   // Eje adelante/atrás
        float turn = Input.GetAxis("Horizontal"); // Eje giro

        // Lógica de mezcla diferencial (Skid-Steering)
        float leftVelocity = (move + turn) * maxLinearSpeed;
        float rightVelocity = (move - turn) * maxLinearSpeed;

        AplicarVelocidad(leftWheels, leftVelocity);
        AplicarVelocidad(rightWheels, rightVelocity);

        //Monitoreo de velocidad angular
        if (Input.GetKey(KeyCode.Space)) // Presiona Espacio para ver el reporte
        {
            Debug.Log($"IZQ: {leftWheels[0].angularVelocity.x:F2} rad/s | DER: {rightWheels[0].angularVelocity.x:F2} rad/s");
        }
    }

    void AplicarVelocidad(ArticulationBody[] wheels, float velocity)
    {
        foreach (var wheel in wheels)
        {
            var drive = wheel.xDrive;
            drive.targetVelocity = velocity * (180 / Mathf.PI); // Convertir rad/s a deg/s para Unity
            wheel.xDrive = drive;
        }
    }
}