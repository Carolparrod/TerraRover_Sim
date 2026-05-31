using UnityEngine;

public class HuskyManualControl : MonoBehaviour
{
    [Header("Wheel References")]
    public ArticulationBody frontLeftWheel;
    public ArticulationBody frontRightWheel;
    public ArticulationBody rearLeftWheel;
    public ArticulationBody rearRightWheel;

    [Header("Movement Settings")]
    public float maxLinearSpeed = 15.0f; // Velocidad un poco más alegre
    public float maxRotationalSpeed = 20.0f; // Giro rápido
    public float motorForce = 5000f; // Fuerza bruta del motor

    void Start()
    {
        // Configuramos las 4 ruedas automáticamente al arrancar
        SetupWheel(frontLeftWheel);
        SetupWheel(frontRightWheel);
        SetupWheel(rearLeftWheel);
        SetupWheel(rearRightWheel);
    }

    void SetupWheel(ArticulationBody wheel)
    {
        if (wheel == null) return;

        // Accedemos a la configuración del motor (xDrive)
        ArticulationDrive drive = wheel.xDrive;

        // 1. Modo VELOCIDAD
        drive.driveType = ArticulationDriveType.Velocity;

        // 2. STIFFNESS en 0 (No queremos efecto muelle)
        drive.stiffness = 0;

        // 3. DAMPING ALTO (Esta es la "fuerza" para alcanzar la velocidad)
        // Si es bajo, la rueda gira perezosa. Si es alto, responde al instante.
        drive.damping = 1000f;

        // 4. Límite de Fuerza infinito (para que nunca le falte potencia)
        drive.forceLimit = float.MaxValue;

        // Guardamos los cambios
        wheel.xDrive = drive;

        // Ajuste extra: Asegurar fricción correcta si el ArticulationBody tiene configuración propia
        wheel.linearDamping = 0.5f;
        wheel.angularDamping = 0.5f;
        wheel.jointFriction = 0.5f;
        wheel.maxAngularVelocity = 100.0f;  // Por defecto es 7, lo subimos a 100
    }

    void FixedUpdate()
    {
        float move = Input.GetAxis("Vertical");
        float turn = Input.GetAxis("Horizontal");

        float targetLeft = move + turn;
        float targetRight = move - turn;

        float leftSpeed = targetLeft * maxLinearSpeed;
        float rightSpeed = targetRight * maxLinearSpeed;

        // Si giramos sobre el propio eje (Move casi 0, Turn activo)
        if (Mathf.Abs(move) < 0.1f && Mathf.Abs(turn) > 0.1f)
        {
            leftSpeed = turn * maxRotationalSpeed;
            rightSpeed = -turn * maxRotationalSpeed;
        }

        SetWheelVelocity(frontLeftWheel, leftSpeed);
        SetWheelVelocity(rearLeftWheel, leftSpeed);
        SetWheelVelocity(frontRightWheel, rightSpeed);
        SetWheelVelocity(rearRightWheel, rightSpeed);
    }

    void SetWheelVelocity(ArticulationBody wheel, float velocity)
    {
        if (wheel == null) return;
        var drive = wheel.xDrive;
        drive.targetVelocity = velocity;
        wheel.xDrive = drive;
    }
}